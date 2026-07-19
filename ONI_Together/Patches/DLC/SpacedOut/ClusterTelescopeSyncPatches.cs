using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using TUNING;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal static class ClusterDiscoverySync
	{
		private static readonly Dictionary<(ClusterDiscoveryKind, int, int, int, float), int> LastPercent = new();

		public static void ResetSessionState() => LastPercent.Clear();

		internal static bool TryApply(ClusterDiscoveryStatePacket state)
		{
			if (state?.IsWireValid() != true || ClusterGrid.Instance == null)
				return false;
			AxialI location = AxialCoordinateSync.FromQr(state.LocationQ, state.LocationR);
			if (!ClusterGrid.Instance.IsValidCell(location))
				return false;
			return state.Kind == ClusterDiscoveryKind.Fog
				? TryApplyFog(location, state)
				: TryApplyMeteor(location, state);
		}

		internal static void BroadcastFog(ClusterFogOfWarManager.Instance manager, AxialI location)
		{
			if (!MultiplayerSession.IsHostInSession || manager == null)
				return;
			float progress = manager.GetRevealCompleteFraction(location);
			SendIfChanged(new ClusterDiscoveryStatePacket
			{
				Kind = ClusterDiscoveryKind.Fog,
				LocationQ = location.q,
				LocationR = location.r,
				Progress = progress,
				Complete = manager.IsLocationRevealed(location)
			});
		}

		internal static void BroadcastMeteor(ClusterMapMeteorShower.Instance meteor)
		{
			if (!MultiplayerSession.IsHostInSession || meteor == null)
				return;
			AxialI location = meteor.ClusterGridPosition();
			SendIfChanged(new ClusterDiscoveryStatePacket
			{
				Kind = ClusterDiscoveryKind.Meteor,
				LocationQ = location.q,
				LocationR = location.r,
				DestinationWorldId = meteor.DestinationWorldID,
				MeteorArrivalTime = meteor.ArrivalTime,
				Progress = meteor.IdentifyingProgress,
				Complete = meteor.HasBeenIdentified
			});
		}

		private static void SendIfChanged(ClusterDiscoveryStatePacket state)
		{
			if (!TryRecordPercent(state))
				return;
			PacketSender.SendToAllClients(state, PacketSendMode.Reliable);
		}

		internal static bool TryRecordPercent(ClusterDiscoveryStatePacket state)
		{
			if (state?.IsWireValid() != true)
				return false;
			int percent = state.Complete ? 100 : (int)(state.Progress * 100f);
			var key = (state.Kind, state.LocationQ, state.LocationR,
				state.DestinationWorldId, state.MeteorArrivalTime);
			if (LastPercent.TryGetValue(key, out int previous) && previous == percent)
				return false;
			LastPercent[key] = percent;
			return true;
		}

		private static bool TryApplyFog(AxialI location, ClusterDiscoveryStatePacket state)
		{
			ClusterFogOfWarManager.Instance manager =
				SaveGame.Instance?.GetSMI<ClusterFogOfWarManager.Instance>();
			if (manager == null)
				return false;
			float current = manager.GetRevealCompleteFraction(location);
			bool complete = manager.IsLocationRevealed(location);
			if (!ClusterDiscoveryStatePacket.NeedsApply(current, complete, state))
				return true;
			if (state.Complete)
			{
				SpacedOutSyncGuard.Run(() => manager.RevealLocation(location));
				return true;
			}
			Dictionary<AxialI, float> points = Traverse.Create(manager)
				.Field("m_revealPointsByCell").GetValue<Dictionary<AxialI, float>>();
			if (points == null)
				return false;
			points[location] = state.Progress * ROCKETRY.CLUSTER_FOW.POINTS_TO_REVEAL;
			return true;
		}

		private static bool TryApplyMeteor(AxialI location, ClusterDiscoveryStatePacket state)
		{
			if (!ClusterGrid.Instance.cellContents.TryGetValue(location, out var entities))
				return false;
			foreach (ClusterGridEntity entity in entities)
			{
				ClusterMapMeteorShower.Instance meteor = entity.gameObject.GetSMI<ClusterMapMeteorShower.Instance>();
				if (meteor == null || meteor.DestinationWorldID != state.DestinationWorldId ||
				    meteor.ArrivalTime != state.MeteorArrivalTime)
					continue;
				if (!ClusterDiscoveryStatePacket.NeedsApply(
					    meteor.IdentifyingProgress, meteor.HasBeenIdentified, state))
					return true;
				if (state.Complete)
					SpacedOutSyncGuard.Run(meteor.Identify);
				else
					Traverse.Create(meteor).Field("identifyingProgress").SetValue(state.Progress);
				return true;
			}
			return false;
		}
	}

	[HarmonyPatch(typeof(ClusterFogOfWarManager.Instance),
		nameof(ClusterFogOfWarManager.Instance.EarnRevealPointsForLocation))]
	internal static class ClusterFogEarnPatch
	{
		internal static bool Prefix(ref bool __result)
		{
			if (!MultiplayerSession.IsClient)
				return true;
			__result = false;
			return false;
		}

		internal static void Postfix(ClusterFogOfWarManager.Instance __instance, AxialI location)
			=> ClusterDiscoverySync.BroadcastFog(__instance, location);
	}

	[HarmonyPatch(typeof(ClusterMapMeteorShower.Instance),
		nameof(ClusterMapMeteorShower.Instance.ProgressIdentifiction))]
	internal static class ClusterMeteorProgressPatch
	{
		internal static bool Prefix() => !MultiplayerSession.IsClient;

		internal static void Postfix(ClusterMapMeteorShower.Instance __instance)
			=> ClusterDiscoverySync.BroadcastMeteor(__instance);
	}

	[HarmonyPatch(typeof(ClusterMapMeteorShower.Instance), nameof(ClusterMapMeteorShower.Instance.Identify))]
	internal static class ClusterMeteorIdentifyPatch
	{
		internal static bool Prefix() => !MultiplayerSession.IsClient || SpacedOutSyncGuard.IsApplying;

		internal static void Postfix(ClusterMapMeteorShower.Instance __instance)
			=> ClusterDiscoverySync.BroadcastMeteor(__instance);
	}
}
