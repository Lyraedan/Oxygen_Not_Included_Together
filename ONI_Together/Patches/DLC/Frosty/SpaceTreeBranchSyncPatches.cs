using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.Frosty;
using UnityEngine;

namespace ONI_Together.Patches.DLC.Frosty
{
	[HarmonyPatch(typeof(SpaceTreeConfig), "CreatePrefab")]
	internal static class SpaceTreePrefabIdentityPatch
	{
		internal static void Postfix(GameObject __result)
			=> NetworkIdentity.EnsurePersistentPrefabIdentity(__result);
	}

	[HarmonyPatch(typeof(SpaceTreeBranchConfig), "CreatePrefab")]
	internal static class SpaceTreeBranchPrefabIdentityPatch
	{
		internal static void Postfix(GameObject __result)
			=> NetworkIdentity.EnsurePersistentPrefabIdentity(__result);
	}

	internal static class SpaceTreeBranchSync
	{
		internal const int MaxPendingStates = 256;
		internal const int PendingRetryLimit = 20;

		private sealed class PendingState
		{
			internal SpaceTreeBranchStatePacket Packet;
			internal int RetriesRemaining;
		}

		private static readonly Dictionary<int, PendingState> PendingStates = new();
		private static bool retryScheduled;
		private static int retryGeneration;
		internal static int PendingCount => PendingStates.Count;

		public static void ResetSessionState()
		{
			PendingStates.Clear();
			retryScheduled = false;
			retryGeneration++;
		}

		internal static bool ShouldRunGameplay(bool inSession, bool isHost) => !inSession || isHost;

		internal static void Receive(SpaceTreeBranchStatePacket packet)
		{
			if (TryApply(packet))
			{
				PendingStates.Remove(packet.BranchNetId);
				return;
			}
			QueuePending(packet);
			ScheduleRetry();
		}

		internal static void QueuePending(SpaceTreeBranchStatePacket packet)
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

		internal static bool TryApply(SpaceTreeBranchStatePacket packet)
		{
			if (packet == null || !packet.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGet(packet.TrunkNetId, out NetworkIdentity trunkIdentity))
				return false;
			PlantBranchGrower.Instance trunk = trunkIdentity.gameObject.GetSMI<PlantBranchGrower.Instance>();
			if (trunk == null || packet.Slot >= trunk.def.BRANCH_OFFSETS.Length)
				return false;
			PlantBranch.Instance branch = FindBranch(packet);
			if (branch == null)
				return false;
			branch.transform.SetPosition(packet.Position);
			ApplyRelationship(trunk, branch, packet.Slot);
			ApplyGrowth(branch, packet.Growth);
			return true;
		}

		internal static void BroadcastBranches(PlantBranchGrower.Instance trunk)
		{
			if (!MultiplayerSession.IsHostInSession || trunk == null)
				return;
			int trunkNetId = EnsureNetId(trunk.gameObject);
			for (int slot = 0; slot < trunk.def.BRANCH_OFFSETS.Length; slot++)
			{
				GameObject branch = trunk.GetBranch(slot);
				SpaceTreeBranchStatePacket packet = branch == null ? null : Capture(trunk, slot, branch);
				if (packet != null)
					PacketSender.SendToAllClients(packet, PacketSendMode.ReliableImmediate);
			}
		}

		private static PlantBranch.Instance FindBranch(SpaceTreeBranchStatePacket packet)
		{
			if (NetworkIdentityRegistry.TryGet(packet.BranchNetId, out NetworkIdentity identity))
			{
				PlantBranch.Instance branch = identity.gameObject.GetSMI<PlantBranch.Instance>();
				return branch?.gameObject.PrefabID().GetHashCode() == packet.PrefabHash ? branch : null;
			}
			return null;
		}

		private static void ApplyRelationship(
			PlantBranchGrower.Instance trunk,
			PlantBranch.Instance branch,
			int slot)
		{
			if (branch.trunk != trunk)
				branch.SetTrunk(trunk);
			var branches = new KPrefabID[trunk.def.BRANCH_OFFSETS.Length];
			for (int i = 0; i < branches.Length; i++)
				branches[i] = i == slot ? branch.GetComponent<KPrefabID>() : trunk.GetBranch(i)?.GetComponent<KPrefabID>();
			trunk.ManuallyDefineBranchArray(branches);
			trunk.RefreshBranchZPositionOffset(branch.gameObject);
		}

		private static void ApplyGrowth(PlantBranch.Instance branch, float growth)
		{
			IManageGrowingStates growing = branch.GetComponent<IManageGrowingStates>() ??
			                               branch.gameObject.GetSMI<IManageGrowingStates>();
			growing?.OverrideMaturityLevel(growth);
		}

		private static SpaceTreeBranchStatePacket Capture(
			PlantBranchGrower.Instance trunk,
			int slot,
			GameObject branch)
		{
			IManageGrowingStates growing = branch.GetComponent<IManageGrowingStates>() ??
			                               branch.GetSMI<IManageGrowingStates>();
			var packet = new SpaceTreeBranchStatePacket
			{
				TrunkNetId = EnsureNetId(trunk.gameObject),
				Slot = slot,
				BranchNetId = EnsureNetId(branch),
				PrefabHash = branch.PrefabID().GetHashCode(),
				Position = branch.transform.GetPosition(),
				Growth = growing?.PercentGrown() ?? 0f
			};
			return packet.IsWireValid() ? packet : null;
		}

		private static int EnsureNetId(GameObject go)
		{
			NetworkIdentity identity = go.AddOrGet<NetworkIdentity>();
			if (identity.NetId == 0)
				identity.RegisterIdentity();
			identity.EnsureAuthoritativeSpawnBroadcast();
			return identity.NetId;
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
			GameScheduler.Instance.Schedule("SpaceTree branch generic bind", 0.1f, _ =>
			{
				if (generation != retryGeneration)
					return;
				retryScheduled = false;
				RetryPending();
			});
		}
	}

	[HarmonyPatch(typeof(PlantBranchGrower.Instance), nameof(PlantBranchGrower.Instance.SpawnRandomBranch))]
	internal static class SpaceTreeSpawnRandomBranchPatch
	{
		internal static bool Prefix(PlantBranchGrower.Instance __instance)
		{
			if (MultiplayerSession.IsClient)
				SpaceTreeBranchSync.RetryPending();
			return SpaceTreeBranchSync.ShouldRunGameplay(
				MultiplayerSession.InSession, MultiplayerSession.IsHost);
		}

		internal static void Postfix(PlantBranchGrower.Instance __instance, bool __result)
		{
			if (__result)
				SpaceTreeBranchSync.BroadcastBranches(__instance);
		}
	}

	[HarmonyPatch(typeof(PlantBranchGrower.Instance), nameof(PlantBranchGrower.Instance.StartSM))]
	internal static class SpaceTreeStartPatch
	{
		internal static void Postfix(PlantBranchGrower.Instance __instance)
		{
			if (MultiplayerSession.IsClient)
				SpaceTreeBranchSync.RetryPending();
		}
	}
}
