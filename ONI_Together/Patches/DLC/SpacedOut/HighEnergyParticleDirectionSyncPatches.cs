using System;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.SpacedOut;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal static class HighEnergyParticleDirectionSync
	{
		internal static bool IsDirectionValid(EightDirection direction)
			=> direction >= EightDirection.Up && direction <= EightDirection.UpRight;

		internal static bool IsSupportedTargetType(Type type)
			=> type == typeof(HighEnergyParticleSpawner) ||
			   type == typeof(ManualHighEnergyParticleSpawner) ||
			   type == typeof(HighEnergyParticleRedirector);

		internal static bool ShouldRunSetter(
			bool inSession,
			bool localIsHost,
			bool isApplying,
			EightDirection current,
			EightDirection desired)
			=> !inSession || localIsHost || isApplying || current == desired;

		internal static bool BeforeSet(
			IHighEnergyParticleDirection target,
			EightDirection desired,
			out EightDirection previous)
		{
			previous = target.Direction;
			if (!IsDirectionValid(desired))
				return false;
			if (ShouldRunSetter(MultiplayerSession.InSession, MultiplayerSession.IsHost,
				    SpacedOutSyncGuard.IsApplying, previous, desired))
				return true;

			SendRequest(target, previous, desired);
			return false;
		}

		internal static void AfterSet(
			IHighEnergyParticleDirection target,
			EightDirection previous)
		{
			if (target == null || previous == target.Direction || SpacedOutSyncGuard.IsApplying ||
			    !MultiplayerSession.InSession || !MultiplayerSession.IsHost)
				return;
			BroadcastAbsolute(target);
		}

		internal static bool TryHandleRequest(HighEnergyParticleDirectionRequestPacket packet)
		{
			if (packet == null || !packet.IsWireValid() ||
			    !TryResolveTarget(packet.TargetNetId, out IHighEnergyParticleDirection target))
				return false;

			if (target.Direction == packet.ExpectedDirection)
				ApplyDirection(target, packet.DesiredDirection);
			BroadcastAbsolute(target);
			return true;
		}

		internal static bool TryApplyState(int targetNetId, EightDirection direction)
		{
			if (!IsDirectionValid(direction) ||
			    !TryResolveTarget(targetNetId, out IHighEnergyParticleDirection target))
				return false;
			ApplyDirection(target, direction);
			return true;
		}

		private static void ApplyDirection(
			IHighEnergyParticleDirection target,
			EightDirection direction)
		{
			SpacedOutSyncGuard.Run(() =>
			{
				target.Direction = direction;
				if (target is KMonoBehaviour component && DetailsScreen.Instance != null &&
				    DetailsScreen.Instance.target == component.gameObject)
					DetailsScreen.Instance.Refresh(component.gameObject);
			});
		}

		private static void SendRequest(
			IHighEnergyParticleDirection target,
			EightDirection expected,
			EightDirection desired)
		{
			if (!TryGetTargetNetId(target, out int targetNetId))
				return;
			PacketSender.SendToAllOtherPeers(new HighEnergyParticleDirectionRequestPacket
			{
				TargetNetId = targetNetId,
				ExpectedDirection = expected,
				DesiredDirection = desired
			});
		}

		private static void BroadcastAbsolute(IHighEnergyParticleDirection target)
		{
			if (!TryGetTargetNetId(target, out int targetNetId) ||
			    !IsDirectionValid(target.Direction))
				return;
			PacketSender.SendToAllClients(new HighEnergyParticleDirectionStatePacket
			{
				TargetNetId = targetNetId,
				Direction = target.Direction
			}, PacketSendMode.ReliableImmediate);
		}

		private static bool TryGetTargetNetId(
			IHighEnergyParticleDirection target,
			out int targetNetId)
		{
			targetNetId = 0;
			if (target is not KMonoBehaviour component ||
			    !IsSupportedTargetType(component.GetType()))
				return false;
			targetNetId = component.GetNetIdentity()?.NetId ?? 0;
			return targetNetId != 0;
		}

		private static bool TryResolveTarget(
			int targetNetId,
			out IHighEnergyParticleDirection target)
		{
			target = null;
			if (targetNetId == 0 ||
			    !NetworkIdentityRegistry.TryGet(targetNetId, out NetworkIdentity identity) ||
			    identity == null)
				return false;

			target = identity.GetComponent<HighEnergyParticleSpawner>() ??
			         (IHighEnergyParticleDirection)identity.GetComponent<ManualHighEnergyParticleSpawner>() ??
			         identity.GetComponent<HighEnergyParticleRedirector>();
			return target != null && IsSupportedTargetType(target.GetType());
		}
	}

	[HarmonyPatch(typeof(HighEnergyParticleSpawner), nameof(HighEnergyParticleSpawner.Direction), MethodType.Setter)]
	internal static class HighEnergyParticleSpawnerDirectionPatch
	{
		internal static bool Prefix(
			HighEnergyParticleSpawner __instance,
			EightDirection value,
			out EightDirection __state)
			=> HighEnergyParticleDirectionSync.BeforeSet(__instance, value, out __state);

		internal static void Postfix(HighEnergyParticleSpawner __instance, EightDirection __state)
			=> HighEnergyParticleDirectionSync.AfterSet(__instance, __state);
	}

	[HarmonyPatch(typeof(ManualHighEnergyParticleSpawner), nameof(ManualHighEnergyParticleSpawner.Direction), MethodType.Setter)]
	internal static class ManualHighEnergyParticleSpawnerDirectionPatch
	{
		internal static bool Prefix(
			ManualHighEnergyParticleSpawner __instance,
			EightDirection value,
			out EightDirection __state)
			=> HighEnergyParticleDirectionSync.BeforeSet(__instance, value, out __state);

		internal static void Postfix(ManualHighEnergyParticleSpawner __instance, EightDirection __state)
			=> HighEnergyParticleDirectionSync.AfterSet(__instance, __state);
	}

	[HarmonyPatch(typeof(HighEnergyParticleRedirector), nameof(HighEnergyParticleRedirector.Direction), MethodType.Setter)]
	internal static class HighEnergyParticleRedirectorDirectionPatch
	{
		internal static bool Prefix(
			HighEnergyParticleRedirector __instance,
			EightDirection value,
			out EightDirection __state)
			=> HighEnergyParticleDirectionSync.BeforeSet(__instance, value, out __state);

		internal static void Postfix(HighEnergyParticleRedirector __instance, EightDirection __state)
			=> HighEnergyParticleDirectionSync.AfterSet(__instance, __state);
	}
}
