using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.SpacedOut;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal static class ClusterLocationFilterSync
	{
		internal static bool TryCapture(
			LogicClusterLocationSensor sensor,
			out ClusterLocationFilterPacketData data)
		{
			data = null;
			int targetNetId = sensor?.GetNetIdentity()?.NetId ?? 0;
			List<AxialI> locations = GetLocations(sensor);
			if (targetNetId == 0 || locations == null)
				return false;

			data = new ClusterLocationFilterPacketData
			{
				TargetNetId = targetNetId,
				ActiveInSpace = sensor.ActiveInSpace,
				ActiveLocations = Canonicalize(locations)
			};
			return data.IsWireValid();
		}

		internal static bool TryApply(ClusterLocationFilterPacketData data)
		{
			if (data == null || !data.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGetComponent(data.TargetNetId, out LogicClusterLocationSensor sensor) ||
			    sensor == null || ClusterGrid.Instance == null)
				return false;

			foreach (AxialI location in data.ActiveLocations)
			{
				if (!ClusterGrid.Instance.IsValidCell(location) ||
				    ClusterUtil.GetAsteroidWorldIdAtLocation(location) < 0)
					return false;
			}

			List<AxialI> current = GetLocations(sensor);
			if (current == null || !NeedsApply(sensor.ActiveInSpace, current, data))
				return current != null;

			var desired = new HashSet<AxialI>(data.ActiveLocations);
			var existing = new HashSet<AxialI>(current);
			SpacedOutSyncGuard.Run(() =>
			{
				foreach (AxialI location in current)
				{
					if (!desired.Contains(location))
						sensor.SetLocationEnabled(location, false);
				}
				foreach (AxialI location in data.ActiveLocations)
				{
					if (!existing.Contains(location))
						sensor.SetLocationEnabled(location, true);
				}
				if (sensor.ActiveInSpace != data.ActiveInSpace)
					sensor.SetSpaceEnabled(data.ActiveInSpace);
			});
			return true;
		}

		internal static bool NeedsApply(
			bool currentSpace,
			IReadOnlyCollection<AxialI> currentLocations,
			ClusterLocationFilterPacketData target)
		{
			if (currentSpace != target.ActiveInSpace || currentLocations.Count != target.ActiveLocations.Count)
				return true;
			var targetSet = new HashSet<AxialI>(target.ActiveLocations);
			foreach (AxialI location in currentLocations)
			{
				if (!targetSet.Contains(location))
					return true;
			}
			return false;
		}

		internal static List<AxialI> Canonicalize(IEnumerable<AxialI> locations)
		{
			var result = new List<AxialI>(new HashSet<AxialI>(locations));
			result.Sort((left, right) =>
			{
				int q = left.q.CompareTo(right.q);
				return q != 0 ? q : left.r.CompareTo(right.r);
			});
			return result;
		}

		internal static void SendSnapshot(LogicClusterLocationSensor sensor)
		{
			if (SpacedOutSyncGuard.IsApplying || !MultiplayerSession.InSession ||
			    !TryCapture(sensor, out ClusterLocationFilterPacketData data))
				return;

			if (MultiplayerSession.IsHost)
				PacketSender.SendToAllClients(new ClusterLocationFilterStatePacket(data));
			else
				PacketSender.SendToAllOtherPeers(new ClusterLocationFilterRequestPacket(data));
		}

		private static List<AxialI> GetLocations(LogicClusterLocationSensor sensor)
		{
			if (sensor == null)
				return null;
			List<AxialI> stored = Traverse.Create(sensor)
				.Field("activeLocations")
				.GetValue<List<AxialI>>();
			return stored == null ? null : new List<AxialI>(stored);
		}
	}

	[HarmonyPatch(typeof(LogicClusterLocationSensor), nameof(LogicClusterLocationSensor.SetLocationEnabled))]
	internal static class ClusterLocationEnabledPatch
	{
		internal static void Prefix(LogicClusterLocationSensor __instance, AxialI location, out bool __state)
			=> __state = __instance.CheckLocationSelected(location);

		internal static void Postfix(
			LogicClusterLocationSensor __instance,
			bool setting,
			bool __state)
		{
			if (__state != setting)
				ClusterLocationFilterSync.SendSnapshot(__instance);
		}
	}

	[HarmonyPatch(typeof(LogicClusterLocationSensor), nameof(LogicClusterLocationSensor.SetSpaceEnabled))]
	internal static class ClusterSpaceEnabledPatch
	{
		internal static void Prefix(LogicClusterLocationSensor __instance, out bool __state)
			=> __state = __instance.ActiveInSpace;

		internal static void Postfix(
			LogicClusterLocationSensor __instance,
			bool setting,
			bool __state)
		{
			if (__state != setting)
				ClusterLocationFilterSync.SendSnapshot(__instance);
		}
	}

	[HarmonyPatch(typeof(LogicClusterLocationSensor), "OnCopySettings")]
	internal static class ClusterLocationCopySettingsPatch
	{
		internal static void Prefix() => SpacedOutSyncGuard.Begin();

		internal static System.Exception Finalizer(
			System.Exception __exception,
			LogicClusterLocationSensor __instance)
		{
			SpacedOutSyncGuard.End();
			if (__exception == null)
				ClusterLocationFilterSync.SendSnapshot(__instance);
			return __exception;
		}
	}
}
