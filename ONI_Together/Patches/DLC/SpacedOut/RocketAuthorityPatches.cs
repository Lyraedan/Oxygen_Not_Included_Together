using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.SpacedOut;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal static partial class RocketSettingsSync
	{
		private static readonly HashSet<int> PendingRepairs = new();

		internal static void ResetSessionState() => PendingRepairs.Clear();

		internal static bool TryGetRocketTarget(
			RocketClusterDestinationSelector selector,
			out RocketModuleCluster module,
			out NetworkIdentity identity)
		{
			module = null;
			identity = null;
			CraftModuleInterface craft = selector?.GetComponent<CraftModuleInterface>();
			module = craft?.GetPrimaryPilotModule(out _);
			identity = module?.GetNetIdentity();
			return identity != null && identity.NetId != 0 && identity.LifecycleRevision != 0;
		}

		internal static bool TryCaptureCraftState(
			CraftModuleInterface craftInterface,
			RocketSettingsPacketData data)
		{
			Clustercraft craft = craftInterface?.GetComponent<Clustercraft>();
			if (craft == null || data == null)
				return false;
			LaunchPad currentPad = craftInterface.CurrentPad;
			bool shouldHavePad = craft.Status != Clustercraft.CraftStatus.InFlight;
			if ((currentPad != null) != shouldHavePad)
				return false;
			int currentPadNetId = 0;
			if (currentPad != null && !TryGetNetId(currentPad, out currentPadNetId))
				return false;

			data.HasCraftState = true;
			data.CraftLocationQ = craft.Location.q;
			data.CraftLocationR = craft.Location.r;
			data.CraftPhase = ToWirePhase(craft.Status);
			data.HasCurrentPad = currentPad != null;
			data.CurrentPadNetId = currentPadNetId;
			return true;
		}

		internal static bool TryCaptureByTarget(
			RocketSettingsPacketData target,
			out RocketSettingsPacketData state)
		{
			state = null;
			if (target == null || !NetworkIdentityRegistry.TryGet(target.TargetNetId, out var identity))
				return false;
			if (target.TargetKind == RocketSettingsTarget.ControlStation)
				return TryCapture(identity.GetComponent<RocketControlStation>(), out state);
			RocketModuleCluster module = identity.GetComponent<RocketModuleCluster>();
			return TryCapture(
				module?.CraftInterface?.GetClusterDestinationSelector(), out state);
		}

		internal static bool TryApplyRequestedSettings(RocketSettingsPacketData data)
		{
			if (data == null || !data.IsWireValid()
			    || !TryResolveIdentity(data, out NetworkIdentity identity))
				return false;
			if (data.TargetKind == RocketSettingsTarget.ControlStation)
				return ApplyRequestedStation(identity, data);

			RocketModuleCluster module = identity.GetComponent<RocketModuleCluster>();
			RocketClusterDestinationSelector selector =
				module?.CraftInterface?.GetClusterDestinationSelector();
			if (selector == null
			    || !TryResolveDestination(data, out AxialI destination, out LaunchPad pad))
				return false;
			SpacedOutSyncGuard.Run(() => ApplySelector(selector, data, destination, pad));
			return TryCapture(selector, out RocketSettingsPacketData current)
			       && SettingsSnapshotsMatch(data, current);
		}

		private static bool ApplyRequestedStation(
			NetworkIdentity identity, RocketSettingsPacketData data)
		{
			RocketControlStation station = identity.GetComponent<RocketControlStation>();
			if (station == null)
				return false;
			if (station.RestrictWhenGrounded != data.RestrictWhenGrounded)
				SpacedOutSyncGuard.Run(
					() => station.RestrictWhenGrounded = data.RestrictWhenGrounded);
			return station.RestrictWhenGrounded == data.RestrictWhenGrounded;
		}

		internal static bool TryApplyAuthoritative(RocketSettingsPacketData data)
		{
			if (!CanApplyAuthoritative(data)
			    || !TryResolveIdentity(data, out NetworkIdentity identity))
				return false;
			if (data.TargetKind == RocketSettingsTarget.ControlStation)
				return ApplyRequestedStation(identity, data) && MatchesCurrent(data);

			RocketModuleCluster module = identity.GetComponent<RocketModuleCluster>();
			CraftModuleInterface craftInterface = module?.CraftInterface;
			RocketClusterDestinationSelector selector =
				craftInterface?.GetClusterDestinationSelector();
			TryResolveDestination(data, out AxialI destination, out LaunchPad destinationPad);
			TryResolveCurrentPad(data, out LaunchPad currentPad);
			bool lifecycleApplied = false;
			SpacedOutSyncGuard.Run(() =>
			{
				ApplySelector(selector, data, destination, destinationPad);
				lifecycleApplied = TryApplyCraftState(craftInterface, data, currentPad);
			});
			return lifecycleApplied && MatchesCurrent(data);
		}

		internal static bool CanApplyAuthoritative(RocketSettingsPacketData data)
		{
			if (data == null || !data.IsAuthoritativeWireValid()
			    || !TryResolveIdentity(data, out NetworkIdentity identity))
				return false;
			if (data.TargetKind == RocketSettingsTarget.ControlStation)
				return identity.GetComponent<RocketControlStation>() != null;

			RocketModuleCluster module = identity.GetComponent<RocketModuleCluster>();
			CraftModuleInterface craftInterface = module?.CraftInterface;
			Clustercraft craft = craftInterface?.GetComponent<Clustercraft>();
			if (craft == null || craftInterface.GetClusterDestinationSelector() == null
			    || !TryResolveDestination(data, out _, out _)
			    || !TryResolveCurrentPad(data, out LaunchPad targetPad))
				return false;
			AxialI location = AxialCoordinateSync.FromQr(
				data.CraftLocationQ, data.CraftLocationR);
			return ClusterGrid.Instance != null && ClusterGrid.Instance.IsValidCell(location)
			       && CanStartTransition(craftInterface, craft, data.CraftPhase, targetPad);
		}

		private static bool TryResolveIdentity(
			RocketSettingsPacketData data, out NetworkIdentity identity)
		{
			identity = null;
			if (data?.TargetLifecycleRevision == 0
			    || !NetworkIdentityRegistry.TryGet(data.TargetNetId, out identity)
			    || identity == null || identity.LifecycleRevision != data.TargetLifecycleRevision)
				return false;
			return !NetworkIdentityRegistry.IsLifecycleTombstoned(data.TargetNetId)
			       && NetworkIdentityRegistry.GetLastLifecycleRevision(data.TargetNetId)
			       == data.TargetLifecycleRevision;
		}

		private static bool TryResolveCurrentPad(
			RocketSettingsPacketData data, out LaunchPad pad)
		{
			pad = null;
			if (!data.HasCurrentPad)
				return true;
			if (!NetworkIdentityRegistry.TryGetComponent(data.CurrentPadNetId, out pad)
			    || pad == null)
				return false;
			if (data.CraftPhase == RocketCraftPhase.Launching)
				return true;
			return pad.GetMyWorldLocation() == AxialCoordinateSync.FromQr(
				data.CraftLocationQ, data.CraftLocationR);
		}

		private static bool CanStartTransition(
			CraftModuleInterface craftInterface,
			Clustercraft craft,
			RocketCraftPhase target,
			LaunchPad targetPad)
		{
			RocketCraftPhase current = ToWirePhase(craft.Status);
			if (current == target)
				return CurrentPadMatches(craftInterface, targetPad);
			if (current == RocketCraftPhase.Grounded
			    && (target == RocketCraftPhase.Launching || target == RocketCraftPhase.InFlight))
				return craftInterface.CurrentPad != null
				       && (target != RocketCraftPhase.Launching
				           || craftInterface.CurrentPad == targetPad);
			if (current == RocketCraftPhase.InFlight
			    && (target == RocketCraftPhase.Landing || target == RocketCraftPhase.Grounded))
				return targetPad != null;
			return current == RocketCraftPhase.Launching && target == RocketCraftPhase.InFlight
			       || current == RocketCraftPhase.Landing && target == RocketCraftPhase.Grounded;
		}

		private static bool TryApplyCraftState(
			CraftModuleInterface craftInterface,
			RocketSettingsPacketData data,
			LaunchPad targetPad)
		{
			Clustercraft craft = craftInterface.GetComponent<Clustercraft>();
			RocketCraftPhase current = ToWirePhase(craft.Status);
			AxialI location = AxialCoordinateSync.FromQr(
				data.CraftLocationQ, data.CraftLocationR);
			if (current == data.CraftPhase)
			{
				craft.Location = location;
				return CurrentPadMatches(craftInterface, targetPad);
			}
			if (current == RocketCraftPhase.Grounded
			    && (data.CraftPhase == RocketCraftPhase.Launching
			        || data.CraftPhase == RocketCraftPhase.InFlight))
				return BeginLaunch(craftInterface, craft, data, location, targetPad);
			if (current == RocketCraftPhase.InFlight
			    && (data.CraftPhase == RocketCraftPhase.Landing
			        || data.CraftPhase == RocketCraftPhase.Grounded))
				return BeginLanding(craftInterface, craft, data, location, targetPad);
			craft.Location = location;
			return false;
		}

		private static bool BeginLaunch(
			CraftModuleInterface craftInterface,
			Clustercraft craft,
			RocketSettingsPacketData data,
			AxialI location,
			LaunchPad targetPad)
		{
			LaunchPad localPad = craftInterface.CurrentPad;
			if (localPad == null || data.CraftPhase == RocketCraftPhase.Launching
			    && localPad != targetPad)
				return false;
			craft.SetCraftStatus(Clustercraft.CraftStatus.Launching);
			craftInterface.DoLaunch();
			craft.Location = location;
			return data.CraftPhase == RocketCraftPhase.Launching
			       && CurrentPadMatches(craftInterface, targetPad);
		}

		private static bool BeginLanding(
			CraftModuleInterface craftInterface,
			Clustercraft craft,
			RocketSettingsPacketData data,
			AxialI location,
			LaunchPad targetPad)
		{
			if (targetPad == null
			    || craft.CanLandAtPad(targetPad, out _) != Clustercraft.PadLandingStatus.CanLandImmediately)
				return false;
			craft.Location = location;
			craft.SetCraftStatus(Clustercraft.CraftStatus.Landing);
			craftInterface.DoLand(targetPad);
			return data.CraftPhase == RocketCraftPhase.Landing
			       && CurrentPadMatches(craftInterface, targetPad);
		}

		private static bool CurrentPadMatches(
			CraftModuleInterface craftInterface, LaunchPad targetPad)
			=> craftInterface.CurrentPad == targetPad;

		private static RocketCraftPhase ToWirePhase(Clustercraft.CraftStatus status)
			=> status switch
			{
				Clustercraft.CraftStatus.Grounded => RocketCraftPhase.Grounded,
				Clustercraft.CraftStatus.Launching => RocketCraftPhase.Launching,
				Clustercraft.CraftStatus.InFlight => RocketCraftPhase.InFlight,
				Clustercraft.CraftStatus.Landing => RocketCraftPhase.Landing,
				_ => RocketCraftPhase.None,
			};

		private static bool SettingsSnapshotsMatch(
			RocketSettingsPacketData expected, RocketSettingsPacketData actual)
		{
			if (expected.TargetKind == RocketSettingsTarget.ControlStation)
				return expected.RestrictWhenGrounded == actual.RestrictWhenGrounded;
			return expected.HasDestination == actual.HasDestination
			       && (!expected.HasDestination
			           || expected.DestinationQ == actual.DestinationQ
			           && expected.DestinationR == actual.DestinationR)
			       && expected.HasPad == actual.HasPad
			       && (!expected.HasPad || expected.PadNetId == actual.PadNetId)
			       && expected.Repeat == actual.Repeat;
		}

		internal static bool AuthoritySnapshotsMatch(
			RocketSettingsPacketData expected, RocketSettingsPacketData actual)
			=> expected.HasCraftState == actual.HasCraftState
			   && expected.CraftLocationQ == actual.CraftLocationQ
			   && expected.CraftLocationR == actual.CraftLocationR
			   && expected.CraftPhase == actual.CraftPhase
			   && expected.HasCurrentPad == actual.HasCurrentPad
			   && expected.CurrentPadNetId == actual.CurrentPadNetId;

		internal static void RequestRepair(RocketSettingsPacketData failed)
		{
			if (failed == null || !MultiplayerSession.IsClient
			    || !PendingRepairs.Add(failed.TargetNetId))
				return;
			PacketSender.SendToAllOtherPeers(
				new RocketSettingsRequestPacket(failed, snapshotOnly: true));
		}

		internal static void CompleteRepair(int targetNetId) => PendingRepairs.Remove(targetNetId);

		internal static void OnCraftLifecycleChanged(Clustercraft craft)
		{
			if (craft == null || SpacedOutSyncGuard.IsApplying || !MultiplayerSession.InSession)
				return;
			RocketClusterDestinationSelector selector =
				craft.ModuleInterface?.GetClusterDestinationSelector();
			if (MultiplayerSession.IsHost)
			{
				SendSnapshot(selector);
				return;
			}
			RetryPendingRepair(selector);
		}

		private static void RetryPendingRepair(RocketClusterDestinationSelector selector)
		{
			if (!TryCapture(selector, out RocketSettingsPacketData state)
			    || !PendingRepairs.Contains(state.TargetNetId))
				return;
			PacketSender.SendToAllOtherPeers(
				new RocketSettingsRequestPacket(state, snapshotOnly: true));
		}
	}

	[HarmonyPatch(typeof(Clustercraft), nameof(Clustercraft.SetCraftStatus))]
	internal static class RocketCraftStatusAuthorityPatch
	{
		internal static void Postfix(Clustercraft __instance)
			=> RocketSettingsSync.OnCraftLifecycleChanged(__instance);
	}

	[HarmonyPatch(typeof(CraftModuleInterface), nameof(CraftModuleInterface.DoLaunch))]
	internal static class RocketCraftLaunchAuthorityPatch
	{
		internal static void Postfix(CraftModuleInterface __instance)
			=> RocketSettingsSync.OnCraftLifecycleChanged(
				__instance.GetComponent<Clustercraft>());
	}

	[HarmonyPatch(typeof(CraftModuleInterface), nameof(CraftModuleInterface.DoLand))]
	internal static class RocketCraftLandAuthorityPatch
	{
		internal static void Postfix(CraftModuleInterface __instance)
			=> RocketSettingsSync.OnCraftLifecycleChanged(
				__instance.GetComponent<Clustercraft>());
	}

	[HarmonyPatch(typeof(ClusterGridEntity), nameof(ClusterGridEntity.Location), MethodType.Setter)]
	internal static class RocketCraftLocationAuthorityPatch
	{
		internal static void Postfix(ClusterGridEntity __instance)
		{
			if (__instance is Clustercraft craft)
				RocketSettingsSync.OnCraftLifecycleChanged(craft);
		}
	}
}
