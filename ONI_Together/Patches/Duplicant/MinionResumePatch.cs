using System;
using HarmonyLib;
using ONI_Together.Networking;

namespace ONI_Together.Patches.Duplicant;

internal static class SkillResumePatchGate
{
	internal static bool BeginMaster(
		MinionResume resume,
		string skillId,
		out SkillResumeMutationScope scope)
	{
		scope = default;
		bool inSession = MultiplayerSession.InSession;
		bool isHost = MultiplayerSession.IsHost;
		bool applying = SkillResumeSync.IsApplyingSnapshot;
		if (SkillResumeSync.ShouldSendMasteryRequest(inSession, isHost, applying))
		{
			SkillResumeSync.SendMasteryRequest(resume, skillId);
			return false;
		}
		if (!SkillResumeSync.ShouldRunLocally(inSession, isHost, applying))
			return false;
		scope = SkillResumeSync.BeginHostMutation(resume);
		return true;
	}

	internal static bool BeginAuthoritative(
		MinionResume resume,
		out SkillResumeMutationScope scope)
	{
		scope = default;
		if (!SkillResumeSync.ShouldRunLocally(
			    MultiplayerSession.InSession,
			    MultiplayerSession.IsHost,
			    SkillResumeSync.IsApplyingSnapshot))
			return false;
		scope = SkillResumeSync.BeginHostMutation(resume);
		return true;
	}

	internal static Exception Finish(Exception error, SkillResumeMutationScope scope)
	{
		SkillResumeSync.CompleteHostMutation(scope, error);
		return error;
	}
}

[HarmonyPatch(typeof(MinionResume), nameof(MinionResume.SetHats), typeof(string), typeof(string))]
internal static class MinionResumeSetHatsPatch
{
	public static bool Prefix(
		MinionResume __instance,
		string target,
		out SkillResumeMutationScope __state)
	{
		__state = default;
		if (SkillResumeSync.ShouldSendMasteryRequest(
			    MultiplayerSession.InSession,
			    MultiplayerSession.IsHost,
			    SkillResumeSync.IsApplyingSnapshot))
		{
			SkillResumeSync.SendHatRequest(__instance, target);
			return false;
		}
		return SkillResumePatchGate.BeginAuthoritative(__instance, out __state);
	}

	public static Exception Finalizer(Exception __exception, SkillResumeMutationScope __state)
		=> SkillResumePatchGate.Finish(__exception, __state);
}

[HarmonyPatch(typeof(MinionResume), nameof(MinionResume.ApplyTargetHat))]
internal static class MinionResumeApplyTargetHatPatch
{
	public static bool Prefix(MinionResume __instance, out SkillResumeMutationScope __state)
		=> SkillResumePatchGate.BeginAuthoritative(__instance, out __state);

	public static Exception Finalizer(Exception __exception, SkillResumeMutationScope __state)
		=> SkillResumePatchGate.Finish(__exception, __state);
}

[HarmonyPatch(typeof(MinionResume), nameof(MinionResume.CreateHatChangeChore))]
internal static class MinionResumeCreateHatChangeChorePatch
{
	public static bool Prefix()
		=> SkillResumeSync.ShouldRunLocally(
			MultiplayerSession.InSession,
			MultiplayerSession.IsHost,
			SkillResumeSync.IsApplyingSnapshot);
}

[HarmonyPatch(typeof(MinionResume), nameof(MinionResume.MasterSkill), typeof(string))]
internal static class MinionResumeMasterSkillPatch
{
	public static bool Prefix(
		MinionResume __instance,
		string skillId,
		out SkillResumeMutationScope __state)
		=> SkillResumePatchGate.BeginMaster(__instance, skillId, out __state);

	public static Exception Finalizer(Exception __exception, SkillResumeMutationScope __state)
		=> SkillResumePatchGate.Finish(__exception, __state);
}

[HarmonyPatch(typeof(MinionResume), nameof(MinionResume.UnmasterSkill), typeof(string))]
internal static class MinionResumeUnmasterSkillPatch
{
	public static bool Prefix(MinionResume __instance, out SkillResumeMutationScope __state)
		=> SkillResumePatchGate.BeginAuthoritative(__instance, out __state);

	public static Exception Finalizer(Exception __exception, SkillResumeMutationScope __state)
		=> SkillResumePatchGate.Finish(__exception, __state);
}

[HarmonyPatch(typeof(MinionResume), nameof(MinionResume.GrantSkill), typeof(string))]
internal static class MinionResumeGrantSkillPatch
{
	public static bool Prefix(MinionResume __instance, out SkillResumeMutationScope __state)
		=> SkillResumePatchGate.BeginAuthoritative(__instance, out __state);

	public static Exception Finalizer(Exception __exception, SkillResumeMutationScope __state)
		=> SkillResumePatchGate.Finish(__exception, __state);
}

[HarmonyPatch(typeof(MinionResume), nameof(MinionResume.UngrantSkill), typeof(string))]
internal static class MinionResumeUngrantSkillPatch
{
	public static bool Prefix(MinionResume __instance, out SkillResumeMutationScope __state)
		=> SkillResumePatchGate.BeginAuthoritative(__instance, out __state);

	public static Exception Finalizer(Exception __exception, SkillResumeMutationScope __state)
		=> SkillResumePatchGate.Finish(__exception, __state);
}
