using System.Collections.Generic;
using HarmonyLib;
using Klei.AI;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.Aquatic;
using UnityEngine;

namespace ONI_Together.Patches.DLC.Aquatic
{
	[HarmonyPatch(typeof(SeaTreeRootConfig), "CreatePrefab")]
	internal static class SeaTreeRootPrefabIdentityPatch
	{
		internal static void Postfix(GameObject __result)
			=> NetworkIdentity.EnsurePersistentPrefabIdentity(__result);
	}

	[HarmonyPatch(typeof(SeaTreeBranchConfig), "CreatePrefab")]
	internal static class SeaTreeBranchPrefabIdentityPatch
	{
		internal static void Postfix(GameObject __result)
			=> NetworkIdentity.EnsurePersistentPrefabIdentity(__result);
	}

	internal readonly struct SeaTreeAmounts
	{
		internal readonly float Maturity;
		internal readonly float FruitMaturity;
		internal readonly float OldAge;

		internal SeaTreeAmounts(float maturity, float fruitMaturity, float oldAge)
		{
			Maturity = maturity;
			FruitMaturity = fruitMaturity;
			OldAge = oldAge;
		}
	}

	internal readonly struct SeaTreeAmountHandles
	{
		internal readonly AmountInstance Maturity;
		internal readonly AmountInstance FruitMaturity;
		internal readonly AmountInstance OldAge;

		internal SeaTreeAmountHandles(AmountInstance maturity, AmountInstance fruitMaturity, AmountInstance oldAge)
		{
			Maturity = maturity;
			FruitMaturity = fruitMaturity;
			OldAge = oldAge;
		}
	}

	internal static class SeaTreeBranchSync
	{
		internal const int MaxPendingStates = 256;
		internal const int PendingRetryLimit = 20;

		private sealed class PendingState
		{
			internal SeaTreeBranchStatePacket Packet;
			internal int RetriesRemaining;
		}

		private static readonly Dictionary<int, int> HostFruitSequences = new();
		private static readonly Dictionary<int, PendingState> PendingStates = new();
		private static bool retryScheduled;
		private static int retryGeneration;
		internal static int PendingCount => PendingStates.Count;

		public static void ResetSessionState()
		{
			HostFruitSequences.Clear();
			PendingStates.Clear();
			retryScheduled = false;
			retryGeneration++;
		}

		internal static int NextFruitSequence(int branchNetId, bool fruitOutcome)
		{
			HostFruitSequences.TryGetValue(branchNetId, out int sequence);
			if (fruitOutcome)
				HostFruitSequences[branchNetId] = ++sequence;
			return sequence;
		}

		internal static bool NeedsAmountApply(SeaTreeAmounts current, SeaTreeAmounts target)
			=> current.Maturity != target.Maturity || current.FruitMaturity != target.FruitMaturity ||
			   current.OldAge != target.OldAge;

		internal static void SendRootBranch(SeaTreeRoot.Instance root)
		{
			if (!MultiplayerSession.IsHostInSession || root?.Branch == null)
				return;
			SendState(root.Branch.GetSMI<SeaTreeBranch.Instance>(), false);
		}

		internal static void SendBranchAndChild(SeaTreeBranch.Instance branch)
		{
			if (!MultiplayerSession.IsHostInSession || branch == null)
				return;
			if (branch.BranchSMI != null)
				SendState(branch.BranchSMI, false);
			SendState(branch, false);
		}

		internal static void SendState(SeaTreeBranch.Instance branch, bool fruitOutcome)
		{
			if (!MultiplayerSession.IsHostInSession || !TryBuildPacket(branch, out var packet))
				return;

			packet.FruitSequence = NextFruitSequence(packet.BranchNetId, fruitOutcome);
			PacketSender.SendToAllClients(packet, PacketSendMode.ReliableImmediate);
		}

		internal static void Receive(SeaTreeBranchStatePacket packet)
		{
			if (TryApply(packet))
			{
				PendingStates.Remove(packet.BranchNetId);
				return;
			}
			QueuePending(packet);
			ScheduleRetry();
		}

		internal static bool TryApply(SeaTreeBranchStatePacket packet)
		{
			if (packet == null || !packet.IsWireValid() ||
			    !TryGetRoot(packet.RootNetId, out SeaTreeRoot.Instance root) ||
			    !TryGetBranch(packet, out SeaTreeBranch.Instance branch))
				return false;

			branch.transform.SetPosition(packet.Position);
			if (!ApplyRelationship(packet, root, branch))
				return false;
			bool newFruitOutcome = SeaTreeBranchStatePacket.TryClaimFruitSequence(
				packet.BranchNetId, packet.FruitSequence);
			bool amountsChanged = ApplyAmounts(branch, packet);
			ApplyChild(packet.ChildNetId, branch);
			if (amountsChanged || newFruitOutcome)
				RefreshVisuals(branch);
			return true;
		}

		private static bool TryBuildPacket(SeaTreeBranch.Instance branch, out SeaTreeBranchStatePacket packet)
		{
			packet = null;
			SeaTreeRoot.Instance root = branch?.Root?.GetSMI<SeaTreeRoot.Instance>();
			if (root == null || !TryCaptureAmounts(branch, out SeaTreeAmounts amounts))
				return false;

			int rootNetId = EnsureNetId(root.gameObject);
			int branchNetId = EnsureNetId(branch.gameObject);
			if (rootNetId == 0 || branchNetId == 0)
				return false;

			SeaTreeBranch.Instance previous = FindPrevious(root, branch);
			packet = new SeaTreeBranchStatePacket
			{
				RootNetId = rootNetId,
				PreviousNetId = EnsureNetId(previous?.gameObject),
				BranchNetId = branchNetId,
				ChildNetId = EnsureNetId(branch.Branch),
				PrefabHash = branch.gameObject.PrefabID().GetHashCode(),
				Position = branch.transform.GetPosition(),
				Maturity = amounts.Maturity,
				FruitMaturity = amounts.FruitMaturity,
				OldAge = amounts.OldAge
			};
			return packet.IsWireValid();
		}

		private static SeaTreeBranch.Instance FindPrevious(SeaTreeRoot.Instance root, SeaTreeBranch.Instance target)
		{
			SeaTreeBranch.Instance current = root.Branch?.GetSMI<SeaTreeBranch.Instance>();
			SeaTreeBranch.Instance previous = null;
			for (int i = 0; i < 8 && current != null; i++)
			{
				if (current == target)
					return previous;
				previous = current;
				current = current.BranchSMI;
			}
			return null;
		}

		private static int EnsureNetId(GameObject go)
		{
			if (go == null)
				return 0;
			NetworkIdentity identity = go.AddOrGet<NetworkIdentity>();
			if (identity.NetId == 0)
				identity.RegisterIdentity();
			identity.EnsureAuthoritativeSpawnBroadcast();
			return identity.NetId;
		}

		private static bool TryGetRoot(int netId, out SeaTreeRoot.Instance root)
		{
			root = null;
			if (!NetworkIdentityRegistry.TryGet(netId, out NetworkIdentity identity))
				return false;
			root = identity.gameObject.GetSMI<SeaTreeRoot.Instance>();
			return root != null;
		}

		private static bool TryGetBranch(
			SeaTreeBranchStatePacket packet,
			out SeaTreeBranch.Instance branch)
		{
			branch = null;
			if (!NetworkIdentityRegistry.TryGet(packet.BranchNetId, out NetworkIdentity identity) ||
			    identity.gameObject.PrefabID().GetHashCode() != packet.PrefabHash)
				return false;
			branch = identity.gameObject.GetSMI<SeaTreeBranch.Instance>();
			return branch != null;
		}

		internal static void QueuePending(SeaTreeBranchStatePacket packet)
		{
			if (packet == null || !packet.IsWireValid())
				return;
			if (PendingStates.TryGetValue(packet.BranchNetId, out PendingState pending))
			{
				pending.Packet = packet;
				pending.RetriesRemaining = PendingRetryLimit;
				return;
			}
			if (PendingStates.Count >= MaxPendingStates)
				EvictPending();
			PendingStates[packet.BranchNetId] = new PendingState
			{
				Packet = packet,
				RetriesRemaining = PendingRetryLimit
			};
		}

		internal static void RetryPending()
		{
			foreach (var entry in new List<KeyValuePair<int, PendingState>>(PendingStates))
			{
				PendingState pending = entry.Value;
				if (TryApply(pending.Packet) || --pending.RetriesRemaining <= 0)
					PendingStates.Remove(entry.Key);
			}
			if (PendingStates.Count > 0)
				ScheduleRetry();
		}

		private static void EvictPending()
		{
			foreach (int netId in PendingStates.Keys)
			{
				PendingStates.Remove(netId);
				return;
			}
		}

		private static void ScheduleRetry()
		{
			if (retryScheduled || GameScheduler.Instance == null)
				return;
			retryScheduled = true;
			int generation = retryGeneration;
			GameScheduler.Instance.Schedule("SeaTree branch generic bind", 0.1f, _ =>
			{
				if (generation != retryGeneration)
					return;
				retryScheduled = false;
				RetryPending();
			});
		}

		private static bool ApplyRelationship(
			SeaTreeBranchStatePacket packet,
			SeaTreeRoot.Instance root,
			SeaTreeBranch.Instance branch)
		{
			if (packet.PreviousNetId == 0)
			{
				if (branch.Root != root.gameObject || root.Branch != branch.gameObject)
					branch.SetupRootInformation(root);
				root.sm.Branch.Set(branch.gameObject, root);
				return true;
			}

			if (!NetworkIdentityRegistry.TryGet(packet.PreviousNetId, out NetworkIdentity identity))
				return false;
			SeaTreeBranch.Instance previous = identity.gameObject.GetSMI<SeaTreeBranch.Instance>();
			if (previous == null)
				return false;
			if (branch.Root != root.gameObject || previous.Branch != branch.gameObject)
				branch.SetupFromPreviousBranchInformation(previous);
			previous.sm.Branch.Set(branch.gameObject, previous);
			return true;
		}

		private static void ApplyChild(int childNetId, SeaTreeBranch.Instance branch)
		{
			GameObject child = null;
			if (childNetId != 0 && NetworkIdentityRegistry.TryGet(childNetId, out NetworkIdentity identity))
				child = identity.gameObject;
			if (branch.Branch != child)
				branch.sm.Branch.Set(child, branch);
		}

		private static bool ApplyAmounts(SeaTreeBranch.Instance branch, SeaTreeBranchStatePacket packet)
		{
			if (!TryGetAmountHandles(branch, out SeaTreeAmountHandles handles))
				return false;
			var current = new SeaTreeAmounts(
				handles.Maturity.value,
				handles.FruitMaturity.value,
				handles.OldAge.value);
			var target = new SeaTreeAmounts(packet.Maturity, packet.FruitMaturity, packet.OldAge);
			if (!NeedsAmountApply(current, target))
				return false;
			handles.Maturity.SetValue(target.Maturity);
			handles.FruitMaturity.SetValue(target.FruitMaturity);
			handles.OldAge.SetValue(target.OldAge);
			return true;
		}

		private static bool TryCaptureAmounts(SeaTreeBranch.Instance branch, out SeaTreeAmounts amounts)
		{
			amounts = default;
			if (!TryGetAmountHandles(branch, out SeaTreeAmountHandles handles))
				return false;
			amounts = new SeaTreeAmounts(
				handles.Maturity.value,
				handles.FruitMaturity.value,
				handles.OldAge.value);
			return true;
		}

		private static bool TryGetAmountHandles(
			SeaTreeBranch.Instance branch,
			out SeaTreeAmountHandles handles)
		{
			Amounts amounts = branch.gameObject.GetAmounts();
			AmountInstance maturity = amounts?.Get(Db.Get().Amounts.Maturity);
			AmountInstance fruit = amounts?.Get(Db.Get().Amounts.Maturity2);
			AmountInstance oldAge = amounts?.Get(Db.Get().Amounts.OldAge);
			handles = new SeaTreeAmountHandles(maturity, fruit, oldAge);
			return maturity != null && fruit != null && oldAge != null;
		}

		private static void RefreshVisuals(SeaTreeBranch.Instance branch)
		{
			branch.animController.SetPositionPercent(branch.GrowthPercentage);
			branch.UpdateFruitGrowMeterPosition();
		}
	}

	[HarmonyPatch(typeof(SeaTreeRoot.Instance), nameof(SeaTreeRoot.Instance.AttemptToSpawnBranches))]
	internal static class SeaTreeRootSpawnBranchPatch
	{
		internal static bool Prefix()
			=> AquaticSync.ShouldRunAuthoritativeGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(SeaTreeRoot.Instance __instance)
			=> SeaTreeBranchSync.SendRootBranch(__instance);
	}

	[HarmonyPatch(typeof(SeaTreeBranch.Instance), nameof(SeaTreeBranch.Instance.AttemptToSpawnBranch))]
	internal static class SeaTreeBranchSpawnPatch
	{
		internal static bool Prefix()
			=> AquaticSync.ShouldRunAuthoritativeGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(SeaTreeBranch.Instance __instance)
			=> SeaTreeBranchSync.SendBranchAndChild(__instance);
	}

	[HarmonyPatch(typeof(SeaTreeBranch.Instance), "OnSpawnedByDiscovery", typeof(object))]
	internal static class SeaTreeDiscoveryPatch
	{
		internal static bool Prefix()
			=> AquaticSync.ShouldRunAuthoritativeGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(SeaTreeBranch.Instance __instance)
			=> SeaTreeBranchSync.SendState(__instance, false);
	}

	[HarmonyPatch(typeof(SeaTreeBranch.Instance), nameof(SeaTreeBranch.Instance.SpawnCritter))]
	internal static class SeaTreeFruitOutcomePatch
	{
		internal static bool Prefix()
			=> AquaticSync.ShouldRunAuthoritativeGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(SeaTreeBranch.Instance __instance)
			=> SeaTreeBranchSync.SendState(__instance, true);
	}
}
