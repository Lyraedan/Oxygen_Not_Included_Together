using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World.SideScreen
{
	/// <summary>
	/// Patches for SuitMarker (checkpoint) clearance settings synchronization.
	/// Controls whether duplicants can traverse only when suit locker room is available.
	/// </summary>

	[HarmonyPatch(typeof(SuitMarker), "OnEnableTraverseIfUnequipAvailable")]
	public static class SuitMarker_OnEnableTraverse_Patch
	{
		public static void Postfix(SuitMarker __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("SuitMarkerTraversal"),
				Value = 1f, // 1 = Only when room available
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[SuitMarker_OnEnableTraverse_Patch] Synced clearance=OnlyWhenRoomAvailable on {__instance.name}");
		}
	}

	[HarmonyPatch(typeof(SuitMarker), "OnDisableTraverseIfUnequipAvailable")]
	public static class SuitMarker_OnDisableTraverse_Patch
	{
		public static void Postfix(SuitMarker __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("SuitMarkerTraversal"),
				Value = 0f, // 0 = Always allow
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[SuitMarker_OnDisableTraverse_Patch] Synced clearance=Always on {__instance.name}");
		}
	}
}
