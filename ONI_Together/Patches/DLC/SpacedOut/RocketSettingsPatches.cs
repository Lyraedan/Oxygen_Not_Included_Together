using System;
using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.SpacedOut;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal static class SpacedOutSyncGuard
	{
		private static int _applyDepth;

		internal static bool IsApplying => _applyDepth > 0;
		public static void ResetSessionState()
		{
			_applyDepth = 0;
			RocketSettingsSync.ResetSessionState();
			RocketSettingsStatePacket.ResetSessionState();
		}
		internal static void Begin() => _applyDepth++;
		internal static void End()
		{
			if (_applyDepth > 0)
				_applyDepth--;
		}

		internal static void Run(System.Action action)
		{
			Begin();
			try
			{
				action();
			}
			finally
			{
				End();
			}
		}
	}

	internal static partial class RocketSettingsSync
	{
		internal static bool TryCapture(
			RocketClusterDestinationSelector selector,
			out RocketSettingsPacketData data)
		{
			data = null;
			if (selector == null || !TryGetRocketTarget(
				    selector, out RocketModuleCluster module, out NetworkIdentity identity))
				return false;

			AxialI destination = selector.GetDestination();
			bool hasDestination = destination != AxialI.INVALID;
			LaunchPad pad = hasDestination ? selector.GetDestinationPad() : null;
			int padNetId = 0;
			if (pad != null && !TryGetNetId(pad, out padNetId))
				return false;

			data = new RocketSettingsPacketData
			{
				TargetKind = RocketSettingsTarget.DestinationSelector,
				TargetNetId = identity.NetId,
				TargetLifecycleRevision = identity.LifecycleRevision,
				HasDestination = hasDestination,
				DestinationQ = hasDestination ? destination.q : 0,
				DestinationR = hasDestination ? destination.r : 0,
				HasPad = pad != null,
				PadNetId = padNetId,
				Repeat = selector.Repeat
			};
			return TryCaptureCraftState(module.CraftInterface, data) && data.IsWireValid();
		}

		internal static bool TryCapture(RocketControlStation station, out RocketSettingsPacketData data)
		{
			data = null;
			if (station == null || !TryGetNetId(station, out int targetNetId))
				return false;
			NetworkIdentity identity = station.GetNetIdentity();
			if (identity == null || identity.LifecycleRevision == 0)
				return false;

			data = new RocketSettingsPacketData
			{
				TargetKind = RocketSettingsTarget.ControlStation,
				TargetNetId = targetNetId,
				TargetLifecycleRevision = identity.LifecycleRevision,
				RestrictWhenGrounded = station.RestrictWhenGrounded
			};
			return data.IsAuthoritativeWireValid();
		}

		internal static bool TryApply(RocketSettingsPacketData data)
			=> TryApplyAuthoritative(data);

		internal static bool SnapshotsMatch(
			RocketSettingsPacketData expected, RocketSettingsPacketData actual)
		{
			if (expected == null || actual == null
			    || expected.TargetKind != actual.TargetKind
			    || expected.TargetNetId != actual.TargetNetId
			    || expected.TargetLifecycleRevision != actual.TargetLifecycleRevision)
				return false;
			if (expected.TargetKind == RocketSettingsTarget.ControlStation)
				return expected.RestrictWhenGrounded == actual.RestrictWhenGrounded;
			return expected.HasDestination == actual.HasDestination
			       && (!expected.HasDestination
			           || expected.DestinationQ == actual.DestinationQ
			           && expected.DestinationR == actual.DestinationR)
			       && expected.HasPad == actual.HasPad
			       && (!expected.HasPad || expected.PadNetId == actual.PadNetId)
			       && expected.Repeat == actual.Repeat
			       && AuthoritySnapshotsMatch(expected, actual);
		}

		private static bool MatchesCurrent(RocketSettingsPacketData expected)
		{
			if (expected == null
			    || !NetworkIdentityRegistry.TryGet(expected.TargetNetId, out var identity))
				return false;
			if (expected.TargetKind == RocketSettingsTarget.ControlStation)
				return RocketSettingsSync.TryCapture(
					       identity.GetComponent<RocketControlStation>(), out var current)
				       && SnapshotsMatch(expected, current);
			RocketModuleCluster module = identity.GetComponent<RocketModuleCluster>();
			return RocketSettingsSync.TryCapture(
				       module?.CraftInterface?.GetClusterDestinationSelector(), out var selectorState)
			       && SnapshotsMatch(expected, selectorState);
		}

		internal static bool CanApply(RocketSettingsPacketData data)
			=> CanApplyAuthoritative(data);

		internal static bool NeedsApply(
			AxialI currentDestination,
			int currentPadNetId,
			bool currentRepeat,
			RocketSettingsPacketData target)
		{
			AxialI targetDestination = target.HasDestination
				? AxialCoordinateSync.FromQr(target.DestinationQ, target.DestinationR)
				: AxialI.INVALID;
			return currentDestination != targetDestination ||
			       currentPadNetId != (target.HasPad ? target.PadNetId : 0) ||
			       currentRepeat != target.Repeat;
		}

		internal static void SendSnapshot(RocketClusterDestinationSelector selector)
		{
			if (SpacedOutSyncGuard.IsApplying || !MultiplayerSession.InSession ||
			    !TryCapture(selector, out RocketSettingsPacketData data))
				return;

			Send(data);
		}

		internal static void SendSnapshot(RocketControlStation station)
		{
			if (SpacedOutSyncGuard.IsApplying || !MultiplayerSession.InSession ||
			    !TryCapture(station, out RocketSettingsPacketData data))
				return;

			Send(data);
		}

		private static void Send(RocketSettingsPacketData data)
		{
			if (MultiplayerSession.IsHost)
				PacketSender.SendToAllClients(RocketSettingsStatePacket.CreateAuthoritative(data));
			else
				PacketSender.SendToAllOtherPeers(new RocketSettingsRequestPacket(data));
		}

		private static bool TryGetNetId(KMonoBehaviour target, out int netId)
		{
			netId = target?.GetNetIdentity()?.NetId ?? 0;
			return netId != 0;
		}

		private static bool TryResolveDestination(
			RocketSettingsPacketData data,
			out AxialI destination,
			out LaunchPad pad)
		{
			destination = data.HasDestination
				? AxialCoordinateSync.FromQr(data.DestinationQ, data.DestinationR)
				: AxialI.INVALID;
			pad = null;

			if (data.HasDestination &&
			    (ClusterGrid.Instance == null || !ClusterGrid.Instance.IsValidCell(destination)))
				return false;
			if (!data.HasPad)
				return true;

			if (!NetworkIdentityRegistry.TryGetComponent(data.PadNetId, out pad) || pad == null)
				return false;
			return pad.GetMyWorldLocation() == destination;
		}

		private static void ApplySelector(
			RocketClusterDestinationSelector selector,
			RocketSettingsPacketData data,
			AxialI destination,
			LaunchPad pad)
		{
			LaunchPad currentPad = selector.GetDestinationPad();
			int currentPadNetId = currentPad?.GetNetIdentity()?.NetId ?? 0;
			if (!NeedsApply(selector.GetDestination(), currentPadNetId, selector.Repeat, data))
				return;

			if (selector.GetDestination() != destination)
				selector.SetDestination(destination);
			if (selector.Repeat != data.Repeat)
				selector.Repeat = data.Repeat;

			currentPad = selector.GetDestinationPad();
			if (data.HasPad)
			{
				if (currentPad != pad)
					selector.SetDestinationPad(pad);
			}
			else if (currentPad != null)
			{
				ClearDestinationPad(selector, destination);
			}
		}

		private static void ClearDestinationPad(
			RocketClusterDestinationSelector selector,
			AxialI destination)
		{
			var launchPads = Traverse.Create(selector)
				.Field("m_launchPad")
				.GetValue<Dictionary<int, Ref<LaunchPad>>>();
			int worldId = ClusterUtil.GetAsteroidWorldIdAtLocation(destination);
			if (worldId >= 0)
				launchPads?.Remove(worldId);
			selector.SetDestinationPad(null);
		}
	}

	[HarmonyPatch(typeof(RocketClusterDestinationSelector), nameof(RocketClusterDestinationSelector.SetDestination))]
	internal static class RocketDestinationPatch
	{
		internal static void Postfix(RocketClusterDestinationSelector __instance)
			=> RocketSettingsSync.SendSnapshot(__instance);
	}

	[HarmonyPatch(typeof(RocketClusterDestinationSelector), nameof(RocketClusterDestinationSelector.SetDestinationPad))]
	internal static class RocketDestinationPadPatch
	{
		internal static void Postfix(RocketClusterDestinationSelector __instance)
			=> RocketSettingsSync.SendSnapshot(__instance);
	}

	[HarmonyPatch(typeof(RocketClusterDestinationSelector), nameof(RocketClusterDestinationSelector.Repeat), MethodType.Setter)]
	internal static class RocketRepeatPatch
	{
		internal static void Postfix(RocketClusterDestinationSelector __instance)
			=> RocketSettingsSync.SendSnapshot(__instance);
	}

	[HarmonyPatch(typeof(RocketClusterDestinationSelector), "SetUpReturnTrip")]
	internal static class RocketReturnTripPatch
	{
		internal static void Postfix(RocketClusterDestinationSelector __instance)
			=> RocketSettingsSync.SendSnapshot(__instance);
	}

	[HarmonyPatch(typeof(RocketControlStation), nameof(RocketControlStation.RestrictWhenGrounded), MethodType.Setter)]
	internal static class RocketRestrictionPatch
	{
		internal static void Postfix(RocketControlStation __instance)
			=> RocketSettingsSync.SendSnapshot(__instance);
	}
}
