using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using ONI_Together.Networking.Packets.Social;
using UnityEngine;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal static class CryoTankSync
	{
		private static CryoTank CapturingTank;
		private static ImmigrantOptionEntry CapturedStats = ImmigrantOptionEntry.INVALID;

		public static void ResetSessionState()
		{
			CapturingTank = null;
			CapturedStats = ImmigrantOptionEntry.INVALID;
		}

		internal static bool ShouldRunGameplay(bool inSession, bool isHost) => !inSession || isHost;

		internal static void BeginCapture(CryoTank tank)
		{
			CapturingTank = tank;
			CapturedStats = ImmigrantOptionEntry.INVALID;
		}

		internal static void CaptureStats(MinionStartingStats stats)
		{
			if (CapturingTank != null && stats != null)
				CapturedStats = ImmigrantOptionEntry.FromGameDeliverable(stats);
		}

		internal static void EndCapture(CryoTank tank)
		{
			if (tank == null || tank != CapturingTank)
				return;
			CapturingTank = null;
			ImmigrantOptionEntry capturedStats = CapturedStats;
			CapturedStats = ImmigrantOptionEntry.INVALID;
			GameObject minion = tank.smi.sm.defrostedDuplicant.Get(tank.smi);
			if (minion == null || !capturedStats.IsValid)
				return;
			Broadcast(BuildState(tank, minion, capturedStats));
		}

		internal static bool TryApply(CryoTankStatePacket state)
		{
			if (state == null || !state.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGetComponent(state.TargetNetId, out CryoTank tank))
				return false;
			GameObject minion = state.MinionNetId == 0 ? null : ResolveOrCreateMinion(state);
			if (state.MinionNetId != 0 && minion == null)
				return false;
			ApplyRelationships(tank, state, minion);
			ApplyPhase(tank, state.Phase);
			return true;
		}

		private static CryoTankStatePacket BuildState(
			CryoTank tank,
			GameObject minion,
			ImmigrantOptionEntry stats)
		{
			GameObject opener = Traverse.Create(tank).Field("opener").GetValue<GameObject>();
			NetworkIdentity minionIdentity = EnsureIdentity(minion);
			ulong minionRevision = EnsureLifecycle(minionIdentity);
			return new CryoTankStatePacket
			{
				TargetNetId = EnsureNetId(tank.gameObject),
				Phase = CryoTankPhase.Defrost,
				OpenerNetId = opener == null ? 0 : EnsureNetId(opener),
				MinionNetId = minionIdentity.NetId,
				MinionLifecycleRevision = minionRevision,
				Position = minion.transform.GetPosition(),
				ArrivalTime = minion.GetComponent<MinionIdentity>().arrivalTime,
				EntityData = stats
			};
		}

		private static GameObject ResolveOrCreateMinion(CryoTankStatePacket state)
		{
			ulong currentRevision = NetworkIdentityRegistry.GetLastLifecycleRevision(state.MinionNetId);
			bool tombstoned = NetworkIdentityRegistry.IsLifecycleTombstoned(state.MinionNetId);
			if (!CanApplyLifecycle(currentRevision, tombstoned, state.MinionLifecycleRevision))
				return null;
			if (NetworkIdentityRegistry.TryGet(state.MinionNetId, out NetworkIdentity identity))
			{
				if (currentRevision == state.MinionLifecycleRevision)
					return identity.gameObject;
				NetworkIdentityRegistry.Unregister(identity, state.MinionNetId);
				Util.KDestroyGameObject(identity.gameObject);
			}
			if (state.EntityData.ToGameDeliverable() is not MinionStartingStats stats)
				return null;
			GameObject prefab = Assets.GetPrefab(BaseMinionConfig.GetMinionIDForModel(stats.personality.model));
			if (prefab == null)
				return null;
			GameObject minion = Util.KInstantiate(prefab);
			minion.name = prefab.name;
			minion.transform.SetLocalPosition(state.Position);
			minion.SetActive(true);
			stats.Apply(minion);
			Immigration.Instance.ApplyDefaultPersonalPriorities(minion);
			FinishMinionSetup(minion, state.ArrivalTime);
			if (!NetworkIdentityRegistry.TryBindAuthoritativeLifecycle(
				    minion, state.MinionNetId, state.MinionLifecycleRevision))
			{
				Util.KDestroyGameObject(minion);
				return null;
			}
			return minion;
		}

		internal static bool CanApplyLifecycle(
			ulong currentRevision, bool tombstoned, ulong incomingRevision)
		{
			return incomingRevision != 0 && currentRevision <= incomingRevision
			       && (currentRevision != incomingRevision || !tombstoned);
		}

		private static void FinishMinionSetup(GameObject minion, float arrivalTime)
		{
			minion.GetComponent<MinionIdentity>().arrivalTime = arrivalTime;
			MinionResume resume = minion.GetComponent<MinionResume>();
			for (int i = 0; i < 3; i++)
				resume.ForceAddSkillPoint();
			minion.GetComponent<Navigator>().SetCurrentNavType(NavType.Floor);
			minion.GetMyWorld().SetDupeVisited();
			SaveGame.Instance.ColonyAchievementTracker.defrostedDuplicant = true;
		}

		private static void ApplyRelationships(
			CryoTank tank,
			CryoTankStatePacket state,
			GameObject minion)
		{
			GameObject opener = null;
			if (state.OpenerNetId != 0 && NetworkIdentityRegistry.TryGet(state.OpenerNetId, out var identity))
				opener = identity.gameObject;
			Traverse.Create(tank).Field("opener").SetValue(opener);
			if (minion != null)
				tank.smi.sm.defrostedDuplicant.Set(minion, tank.smi);
		}

		private static void ApplyPhase(CryoTank tank, CryoTankPhase phase)
		{
			if (phase == CryoTankPhase.Closed && !tank.smi.IsInsideState(tank.smi.sm.closed))
				tank.smi.GoTo(tank.smi.sm.closed);
			else if (phase == CryoTankPhase.Open && !tank.smi.IsInsideState(tank.smi.sm.open))
				tank.smi.GoTo(tank.smi.sm.open);
			else if (phase == CryoTankPhase.Defrost && !tank.smi.IsInsideState(tank.smi.sm.defrost))
				tank.smi.GoTo(tank.smi.sm.defrost);
			else if (phase == CryoTankPhase.DefrostExit && !tank.smi.IsInsideState(tank.smi.sm.defrostExit))
				tank.smi.GoTo(tank.smi.sm.defrostExit);
			else if (phase == CryoTankPhase.Off && !tank.smi.IsInsideState(tank.smi.sm.off))
				tank.smi.GoTo(tank.smi.sm.off);
		}

		private static int EnsureNetId(GameObject go)
		{
			return EnsureIdentity(go).NetId;
		}

		private static NetworkIdentity EnsureIdentity(GameObject go)
		{
			NetworkIdentity identity = go.AddOrGet<NetworkIdentity>();
			if (identity.NetId == 0)
				identity.RegisterIdentity();
			return identity;
		}

		private static ulong EnsureLifecycle(NetworkIdentity identity)
		{
			if (identity == null || identity.NetId == 0)
				return 0;
			if (identity.LifecycleRevision == 0)
				identity.LifecycleRevision = NetworkIdentityRegistry.BeginLifecycle(identity.NetId);
			return identity.LifecycleRevision;
		}

		private static void Broadcast(CryoTankStatePacket state)
		{
			if (MultiplayerSession.IsHostInSession && state?.IsWireValid() == true)
				PacketSender.SendToAllClients(state, PacketSendMode.ReliableImmediate);
		}
	}

	[HarmonyPatch(typeof(CryoTank), nameof(CryoTank.OnSidescreenButtonPressed))]
		internal static class CryoTankButtonPatch
	{
		internal static bool Prefix(CryoTank __instance)
		{
			if (!MultiplayerSession.IsClient)
				return true;
			int netId = __instance.GetNetId();
			if (netId != 0)
				PacketSender.SendToAllOtherPeers(new CryoTankActivationRequestPacket
			{
				TargetNetId = netId
				});
			return false;
		}
	}

	[HarmonyPatch(typeof(CryoTank), nameof(CryoTank.ActivateChore))]
	internal static class CryoTankActivatePatch
	{
		internal static bool Prefix()
			=> CryoTankSync.ShouldRunGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);

	}

	[HarmonyPatch(typeof(CryoTank), nameof(CryoTank.DropContents))]
	internal static class CryoTankDropContentsPatch
	{
		internal static bool Prefix(CryoTank __instance, out bool __state)
		{
			__state = false;
			bool run = CryoTankSync.ShouldRunGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);
			if (run && MultiplayerSession.IsHostInSession)
			{
				__state = true;
				NetworkIdentity.BeginManagedSpawn();
				CryoTankSync.BeginCapture(__instance);
			}
			return run;
		}

		internal static System.Exception Finalizer(
			CryoTank __instance, System.Exception __exception, bool __state)
		{
			if (!__state)
				return __exception;
			try
			{
				CryoTankSync.EndCapture(__instance);
			}
			finally
			{
				NetworkIdentity.EndManagedSpawn();
			}
			return __exception;
		}
	}

	[HarmonyPatch(typeof(MinionStartingStats), nameof(MinionStartingStats.Apply), typeof(GameObject))]
	internal static class CryoTankStatsCapturePatch
	{
		internal static void Prefix(MinionStartingStats __instance) => CryoTankSync.CaptureStats(__instance);
	}
}
