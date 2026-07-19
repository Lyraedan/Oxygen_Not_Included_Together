using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DuplicantActions;

namespace ONI_Together.Patches.Duplicant;

internal readonly struct SkillResumeMutationScope
{
	internal readonly MinionResume Resume;
	internal readonly bool Entered;

	internal SkillResumeMutationScope(MinionResume resume, bool entered)
	{
		Resume = resume;
		Entered = entered;
	}
}

internal static class SkillResumeSync
{
	private sealed class ResumeRuntimeState
	{
		internal int MutationDepth;
		internal ulong HostRevision;
		internal ulong AppliedRevision;
	}

	private sealed class Reconciliation
	{
		internal MinionResume Resume;
		internal HashSet<string> CurrentMastered;
		internal HashSet<string> CurrentGranted;
		internal HashSet<string> TargetMastered;
		internal HashSet<string> TargetGranted;
	}

	private static ConditionalWeakTable<MinionResume, ResumeRuntimeState> RuntimeStates = new();
	private static int _snapshotApplyDepth;

	internal static bool IsApplyingSnapshot => _snapshotApplyDepth > 0;

	internal static void ResetSessionState()
	{
		RuntimeStates = new ConditionalWeakTable<MinionResume, ResumeRuntimeState>();
		_snapshotApplyDepth = 0;
	}

	internal static bool ShouldRunLocally(bool inSession, bool isHost, bool applyingSnapshot)
		=> !inSession || isHost || applyingSnapshot;

	internal static bool ShouldSendMasteryRequest(bool inSession, bool isHost, bool applyingSnapshot)
		=> inSession && !isHost && !applyingSnapshot;

	internal static bool IsNewerRevision(ulong appliedRevision, ulong incomingRevision)
		=> incomingRevision != 0 && incomingRevision > appliedRevision;

	internal static bool TryResolveResume(int netId, out MinionResume resume)
	{
		resume = null;
		if (netId == 0 || !NetworkIdentityRegistry.TryGet(netId, out NetworkIdentity identity) ||
		    identity == null || identity.NetId != netId ||
		    !identity.TryGetComponent(out MinionIdentity minion) ||
		    !identity.TryGetComponent(out KPrefabID prefab) || !prefab.HasTag(GameTags.BaseMinion))
			return false;
		resume = identity.GetComponent<MinionResume>();
		return resume != null && ReferenceEquals(resume.GetIdentity, minion);
	}

	internal static SkillResumeMutationScope BeginHostMutation(MinionResume resume)
	{
		if (resume == null || !MultiplayerSession.InSession || !MultiplayerSession.IsHost || IsApplyingSnapshot)
			return default;

		ResumeRuntimeState state = RuntimeStates.GetOrCreateValue(resume);
		state.MutationDepth++;
		return new SkillResumeMutationScope(resume, entered: true);
	}

	internal static void CompleteHostMutation(SkillResumeMutationScope scope, Exception error)
	{
		if (!scope.Entered || scope.Resume == null)
			return;

		ResumeRuntimeState state = RuntimeStates.GetOrCreateValue(scope.Resume);
		if (state.MutationDepth > 0)
			state.MutationDepth--;
		if (state.MutationDepth == 0 && error == null)
			SendSnapshot(scope.Resume);
	}

	internal static void SendMasteryRequest(MinionResume resume, string skillId)
	{
		if (resume == null || string.IsNullOrEmpty(skillId) ||
		    !resume.TryGetComponent(out NetworkIdentity identity) || identity.NetId == 0)
			return;

		PacketSender.SendToAllOtherPeers(new SkillMasteryRequestPacket
		{
			NetId = identity.NetId,
			SkillId = skillId
		});
	}

	internal static void SendHatRequest(MinionResume resume, string targetHat)
	{
		if (resume == null || !resume.TryGetComponent(out NetworkIdentity identity) || identity.NetId == 0)
			return;

		PacketSender.SendToAllOtherPeers(new SkillHatRequestPacket
		{
			NetId = identity.NetId,
			TargetHat = targetHat ?? string.Empty
		});
	}

	internal static bool TryApplySnapshot(SkillResumeStateData data)
	{
		if (data == null || !data.IsWireValid() ||
		    !TryResolveResume(data.NetId, out MinionResume resume) || !ValidateGameState(data, resume))
			return false;

		ResumeRuntimeState runtime = RuntimeStates.GetOrCreateValue(resume);
		if (!IsNewerRevision(runtime.AppliedRevision, data.Revision))
			return false;

		_snapshotApplyDepth++;
		try
		{
			ApplyProgression(resume, data);
			runtime.AppliedRevision = data.Revision;
			RefreshSkillsScreen();
			return true;
		}
		catch (Exception error)
		{
			DebugConsole.LogWarning($"[SkillResumeSync] Snapshot apply failed for {data.NetId}: {error}");
			return false;
		}
		finally
		{
			_snapshotApplyDepth--;
		}
	}

	private static void SendSnapshot(MinionResume resume)
	{
		if (TryCaptureSnapshot(resume, out SkillResumeStateData data))
			PacketSender.SendToAllClients(new SkillResumeStatePacket(data));
	}

	private static bool TryCaptureSnapshot(MinionResume resume, out SkillResumeStateData data)
	{
		data = null;
		if (!resume.TryGetComponent(out NetworkIdentity identity) || identity.NetId == 0)
			return false;

		ResumeRuntimeState runtime = RuntimeStates.GetOrCreateValue(resume);
		data = new SkillResumeStateData
		{
			NetId = identity.NetId,
			Revision = ++runtime.HostRevision,
			TotalExperience = resume.TotalExperienceGained,
			AvailableSkillPoints = resume.AvailableSkillpoints,
			MasteredSkillIds = CaptureMasteries(resume),
			GrantedSkillIds = CaptureGrantedSkills(resume),
			Aptitudes = CaptureAptitudes(resume),
			OwnedHats = CaptureOwnedHats(resume),
			CurrentHat = resume.CurrentHat ?? string.Empty,
			TargetHat = resume.TargetHat ?? string.Empty
		};
		return data.IsWireValid();
	}

	private static List<string> CaptureMasteries(MinionResume resume)
		=> resume.MasteryBySkillID
			.Where(entry => entry.Value)
			.Select(entry => entry.Key)
			.OrderBy(id => id, StringComparer.Ordinal)
			.ToList();

	private static List<string> CaptureGrantedSkills(MinionResume resume)
		=> (resume.GrantedSkillIDs ?? new List<string>())
			.Distinct(StringComparer.Ordinal)
			.OrderBy(id => id, StringComparer.Ordinal)
			.ToList();

	private static List<SkillResumeAptitudeData> CaptureAptitudes(MinionResume resume)
		=> resume.AptitudeBySkillGroup
			.OrderBy(entry => entry.Key.hash)
			.Select(entry => new SkillResumeAptitudeData
			{
				SkillGroupHash = entry.Key.hash,
				Amount = entry.Value
			})
			.ToList();

	private static List<SkillResumeHatData> CaptureOwnedHats(MinionResume resume)
	{
		Dictionary<string, bool> hats = Traverse.Create(resume).Field("ownedHats")
			.GetValue<Dictionary<string, bool>>() ?? new Dictionary<string, bool>();
		return hats.OrderBy(entry => entry.Key, StringComparer.Ordinal)
			.Select(entry => new SkillResumeHatData { HatId = entry.Key, IsUnlocked = entry.Value })
			.ToList();
	}

	private static bool ValidateGameState(SkillResumeStateData data, MinionResume resume)
	{
		foreach (string skillId in data.MasteredSkillIds)
		{
			var skill = Db.Get().Skills.TryGet(skillId);
			if (skill == null || skill.deprecated || !Game.IsCorrectDlcActiveForCurrentSave(skill) ||
			    skill.requiredDuplicantModel != null && skill.requiredDuplicantModel != resume.GetIdentity.model)
				return false;
		}

		int expectedAvailable = MinionResume.CalculateTotalSkillPointsGained(data.TotalExperience) -
		                        data.MasteredSkillIds.Count + data.GrantedSkillIds.Count;
		return expectedAvailable == data.AvailableSkillPoints;
	}

	private static void ApplyProgression(MinionResume resume, SkillResumeStateData data)
	{
		var state = new Reconciliation
		{
			Resume = resume,
			TargetMastered = new HashSet<string>(data.MasteredSkillIds, StringComparer.Ordinal),
			TargetGranted = new HashSet<string>(data.GrantedSkillIds, StringComparer.Ordinal),
			CurrentMastered = new HashSet<string>(resume.MasteryBySkillID
				.Where(entry => entry.Value).Select(entry => entry.Key), StringComparer.Ordinal),
			CurrentGranted = new HashSet<string>(resume.GrantedSkillIDs ?? new List<string>(), StringComparer.Ordinal)
		};

		RemoveChangedSkills(state);
		AddChangedSkills(state);
		RestoreAbsoluteFields(resume, data, state.TargetMastered);
	}

	private static void RemoveChangedSkills(Reconciliation state)
	{
		foreach (string skillId in state.CurrentMastered.ToArray())
		{
			if (state.TargetMastered.Contains(skillId) &&
			    state.CurrentGranted.Contains(skillId) == state.TargetGranted.Contains(skillId))
				continue;
			if (state.CurrentGranted.Contains(skillId))
				state.Resume.UngrantSkill(skillId);
			else
				state.Resume.UnmasterSkill(skillId);
			state.CurrentMastered.Remove(skillId);
			state.CurrentGranted.Remove(skillId);
		}
	}

	private static void AddChangedSkills(Reconciliation state)
	{
		foreach (string skillId in state.TargetMastered)
		{
			if (state.CurrentMastered.Contains(skillId))
				continue;
			if (state.TargetGranted.Contains(skillId))
				state.Resume.GrantSkill(skillId);
			else
				state.Resume.MasterSkill(skillId);
		}
	}

	private static void RestoreAbsoluteFields(
		MinionResume resume,
		SkillResumeStateData data,
		HashSet<string> targetMastered)
	{
		var mastery = targetMastered.ToDictionary(id => id, _ => true, StringComparer.Ordinal);
		var aptitudes = data.Aptitudes.ToDictionary(
			entry => new HashedString(entry.SkillGroupHash), entry => entry.Amount);
		resume.RestoreResume(mastery, aptitudes, new List<string>(data.GrantedSkillIds), data.TotalExperience);
		RefreshDerivedProgression(resume);

		var hats = data.OwnedHats.ToDictionary(
			entry => entry.HatId, entry => entry.IsUnlocked, StringComparer.Ordinal);
		Traverse.Create(resume).Field("ownedHats").SetValue(hats);
		ApplyHatState(resume, data.CurrentHat, data.TargetHat);
	}

	private static void RefreshDerivedProgression(MinionResume resume)
	{
		Traverse traversal = Traverse.Create(resume);
		traversal.Method("UpdateExpectations").GetValue();
		traversal.Method("UpdateMorale").GetValue();
	}

	private static void ApplyHatState(MinionResume resume, string currentHat, string targetHat)
	{
		string current = string.IsNullOrEmpty(currentHat) ? null : currentHat;
		string target = string.IsNullOrEmpty(targetHat) ? null : targetHat;
		resume.SetHats(current, target);
		if (resume.TryGetComponent(out KBatchedAnimController controller))
			MinionResume.ApplyHat(current, controller);
	}

	private static void RefreshSkillsScreen()
	{
		SkillsScreen screen = UnityEngine.Object.FindFirstObjectByType<SkillsScreen>();
		if (screen != null)
			screen.RefreshAll();
	}
}
