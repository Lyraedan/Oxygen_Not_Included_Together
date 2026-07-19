using HarmonyLib;
using Klei.AI;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.Prehistoric;
using UnityEngine;

namespace ONI_Together.Patches.DLC.Prehistoric
{
	[HarmonyPatch(typeof(VineMotherConfig), "CreatePrefab")]
	internal static class VineMotherPrefabIdentityPatch
	{
		internal static void Postfix(GameObject __result)
			=> NetworkIdentity.EnsurePersistentPrefabIdentity(__result);
	}

	[HarmonyPatch(typeof(VineBranchConfig), "CreatePrefab")]
	internal static class VineBranchPrefabIdentityPatch
	{
		internal static void Postfix(GameObject __result)
			=> NetworkIdentity.EnsurePersistentPrefabIdentity(__result);
	}

	internal readonly struct VineMotherBranches
	{
		internal readonly GameObject Left;
		internal readonly GameObject Right;

		internal VineMotherBranches(VineMother.Instance mother)
		{
			Left = mother?.LeftBranch;
			Right = mother?.RightBranch;
		}
	}

	internal static class VineBranchSync
	{
		internal const int MaxPendingStates = 256;
		internal const int PendingRetryLimit = 20;

		private sealed class PendingState
		{
			internal VineBranchStatePacket Packet;
			internal int RetriesRemaining;
		}

		private static readonly System.Collections.Generic.Dictionary<int, PendingState> PendingStates = new();
		private static int applyDepth;
		private static bool retryScheduled;
		private static int retryGeneration;
		internal static bool IsApplying => applyDepth > 0;
		internal static int PendingCount => PendingStates.Count;

		public static void ResetSessionState()
		{
			PendingStates.Clear();
			applyDepth = 0;
			retryScheduled = false;
			retryGeneration++;
		}

		internal static bool ShouldRunGameplay(bool inSession, bool isHost, bool applying)
			=> applying || !inSession || isHost;

		internal static void Receive(VineBranchStatePacket packet)
		{
			if (TryApply(packet))
			{
				PendingStates.Remove(packet.BranchNetId);
				return;
			}
			QueuePending(packet);
			ScheduleRetry();
		}

		internal static void SendGraph(VineMother.Instance mother)
		{
			if (!MultiplayerSession.IsHostInSession || mother == null)
				return;
			SendChain(mother, mother.LeftBranch, VineMotherSide.Left);
			SendChain(mother, mother.RightBranch, VineMotherSide.Right);
		}

		internal static bool TryApply(VineBranchStatePacket packet)
		{
			if (packet == null || !packet.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGet(packet.MotherNetId, out NetworkIdentity motherIdentity))
				return false;
			VineMother.Instance mother = motherIdentity.gameObject.GetSMI<VineMother.Instance>();
			if (mother == null || !TryGetBranch(packet, out VineBranch.Instance branch))
				return false;

			VineBranch.Instance previous = null;
			if (packet.PreviousNetId != 0 &&
			    (!NetworkIdentityRegistry.TryGet(packet.PreviousNetId, out NetworkIdentity previousIdentity) ||
			     (previous = previousIdentity.gameObject.GetSMI<VineBranch.Instance>()) == null))
				return false;

			RunApplying(() =>
			{
				branch.transform.SetPosition(packet.Position);
				if (previous == null)
				{
					branch.SetupRootInformation(mother);
					if (packet.MotherSide == VineMotherSide.Left)
						mother.sm.LeftBranch.Set(branch.gameObject, mother);
					else
						mother.sm.RightBranch.Set(branch.gameObject, mother);
				}
				else
				{
					branch.SetupRootInformation(previous);
					previous.sm.Branch.Set(branch.gameObject, previous);
				}

				branch.sm.Mother.Set(mother.gameObject, branch);
				branch.sm.BranchNumber.Set(packet.BranchNumber, branch);
				branch.sm.BranchShape.Set((int)packet.Shape, branch);
				branch.sm.RootShape.Set((int)packet.RootShape, branch);
				branch.sm.RootDirection.Set((int)packet.RootDirection, branch);
				branch.sm.GrowingClockwise.Set(packet.GrowingClockwise, branch);
				branch.sm.WildPlanted.Set(packet.WildPlanted, branch);
				branch.OverrideMaturityLevel(packet.Growth);
				ApplyAmount(branch, Db.Get().Amounts.Maturity2, packet.FruitGrowth);
				ApplyAmount(branch, Db.Get().Amounts.OldAge, packet.OldAge);
				Traverse.Create(branch).Method("SetAnimOrientation", packet.Shape, packet.GrowingClockwise).GetValue();
				branch.ResetUprootMonitor();
			});
			return true;
		}

		private static void SendChain(VineMother.Instance mother, GameObject first, VineMotherSide side)
		{
			VineBranch.Instance previous = null;
			VineBranch.Instance branch = first?.GetSMI<VineBranch.Instance>();
			for (int i = 0; i < 12 && branch != null; i++)
			{
				if (TryCapture(mother, previous, branch, side, out VineBranchStatePacket packet))
					PacketSender.SendToAllClients(packet, PacketSendMode.ReliableImmediate);
				previous = branch;
				branch = branch.BranchSMI;
			}
		}

		private static bool TryCapture(VineMother.Instance mother, VineBranch.Instance previous,
			VineBranch.Instance branch, VineMotherSide side, out VineBranchStatePacket packet)
		{
			packet = null;
			int motherId = EnsureId(mother.gameObject);
			int branchId = EnsureId(branch.gameObject);
			int previousId = EnsureId(previous?.gameObject);
			if (motherId == 0 || branchId == 0)
				return false;
			Amounts amounts = branch.gameObject.GetAmounts();
			packet = new VineBranchStatePacket
			{
				MotherNetId = motherId,
				PreviousNetId = previousId,
				BranchNetId = branchId,
				PrefabHash = branch.gameObject.PrefabID().GetHashCode(),
				Position = branch.transform.GetPosition(),
				MotherSide = side,
				Shape = branch.MyShape,
				RootShape = branch.RootShape,
				RootDirection = branch.RootDirection,
				BranchNumber = branch.MyBranchNumber,
				GrowingClockwise = branch.IsGrowingClockwise,
				WildPlanted = branch.IsWild,
				Growth = Mathf.Clamp01(branch.GrowthPercentage),
				FruitGrowth = CaptureAmount(amounts?.Get(Db.Get().Amounts.Maturity2)),
				OldAge = CaptureAmount(amounts?.Get(Db.Get().Amounts.OldAge))
			};
			return packet.IsWireValid();
		}

		private static bool TryGetBranch(VineBranchStatePacket packet, out VineBranch.Instance branch)
		{
			branch = null;
			if (!NetworkIdentityRegistry.TryGet(packet.BranchNetId, out NetworkIdentity identity) ||
			    identity.gameObject.PrefabID().GetHashCode() != packet.PrefabHash)
				return false;
			branch = identity.gameObject.GetSMI<VineBranch.Instance>();
			return branch != null;
		}

		private static int EnsureId(GameObject go)
		{
			if (go == null)
				return 0;
			NetworkIdentity identity = go.AddOrGet<NetworkIdentity>();
			if (identity.NetId == 0)
				identity.RegisterIdentity();
			identity.EnsureAuthoritativeSpawnBroadcast();
			return identity.NetId;
		}

		internal static void QueuePending(VineBranchStatePacket packet)
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
			foreach (var entry in new System.Collections.Generic.List<
			         System.Collections.Generic.KeyValuePair<int, PendingState>>(PendingStates))
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
			GameScheduler.Instance.Schedule("Vine branch generic bind", 0.1f, _ =>
			{
				if (generation != retryGeneration)
					return;
				retryScheduled = false;
				RetryPending();
			});
		}

		private static float CaptureAmount(Klei.AI.AmountInstance amount)
			=> amount == null || amount.GetMax() <= 0f ? 0f : Mathf.Clamp01(amount.value / amount.GetMax());

		private static void ApplyAmount(VineBranch.Instance branch, Klei.AI.Amount amountId, float percent)
		{
			Klei.AI.AmountInstance amount = branch.gameObject.GetAmounts()?.Get(amountId);
			if (amount != null)
				amount.SetValue(amount.GetMax() * percent);
		}

		private static void RunApplying(System.Action action)
		{
			applyDepth++;
			try { action(); }
			finally { applyDepth--; }
		}
	}

	[HarmonyPatch(typeof(VineMother.Instance), nameof(VineMother.Instance.AttemptToSpawnBranches))]
	internal static class VineMotherSpawnPatch
	{
		internal static bool Prefix(VineMother.Instance __instance, ref VineMotherBranches __state)
		{
			__state = new VineMotherBranches(__instance);
			return VineBranchSync.ShouldRunGameplay(
				MultiplayerSession.InSession, MultiplayerSession.IsHost, VineBranchSync.IsApplying);
		}

		internal static void Postfix(VineMother.Instance __instance, VineMotherBranches __state)
		{
			if (__instance != null && (__state.Left != __instance.LeftBranch || __state.Right != __instance.RightBranch))
				VineBranchSync.SendGraph(__instance);
		}
	}

	[HarmonyPatch(typeof(VineBranch.Instance), nameof(VineBranch.Instance.AttemptToSpawnBranch))]
	internal static class VineBranchSpawnPatch
	{
		internal static bool Prefix(VineBranch.Instance __instance, ref GameObject __state)
		{
			__state = __instance?.Branch;
			return VineBranchSync.ShouldRunGameplay(
				MultiplayerSession.InSession, MultiplayerSession.IsHost, VineBranchSync.IsApplying);
		}

		internal static void Postfix(VineBranch.Instance __instance, GameObject __state)
		{
			if (__instance != null && __state != __instance.Branch)
				VineBranchSync.SendGraph(__instance.Mother?.GetSMI<VineMother.Instance>());
		}
	}

	[HarmonyPatch(typeof(VineBranch.Instance), nameof(VineBranch.Instance.RecalculateMyShape))]
	internal static class VineBranchShapePatch
	{
		internal static bool Prefix() => VineBranchSync.ShouldRunGameplay(
			MultiplayerSession.InSession, MultiplayerSession.IsHost, VineBranchSync.IsApplying);
		internal static void Postfix(VineBranch.Instance __instance)
			=> VineBranchSync.SendGraph(__instance?.Mother?.GetSMI<VineMother.Instance>());
	}

	[HarmonyPatch(typeof(VineBranch.Instance), "OnSpawnedByDiscovery")]
	internal static class VineBranchDiscoveryPatch
	{
		internal static bool Prefix() => VineBranchSync.ShouldRunGameplay(
			MultiplayerSession.InSession, MultiplayerSession.IsHost, VineBranchSync.IsApplying);
		internal static void Postfix(VineBranch.Instance __instance)
			=> VineBranchSync.SendGraph(__instance?.Mother?.GetSMI<VineMother.Instance>());
	}

	[HarmonyPatch(typeof(VineBranch), "RefreshPositionPercent",
		new[] { typeof(VineBranch.Instance), typeof(float) })]
	internal static class VineBranchGrowthPatch
	{
		internal static void Postfix(VineBranch.Instance smi)
			=> VineBranchSync.SendGraph(smi?.Mother?.GetSMI<VineMother.Instance>());
	}
}
