using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Social;
using ONI_Together.Networking.Packets.World;
using System.Collections.Generic;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.GamePatches
{
	/// <summary>
	/// Patch MinionStartingStats.Deliver to send EntitySpawnPacket when duplicants are spawned.
	/// This ensures newly printed duplicants are synced to clients with correct NetIds.
	/// </summary>
	[HarmonyPatch(typeof(MinionStartingStats), nameof(MinionStartingStats.Deliver))]
	public static class MinionDeliverPatch
	{
		public static void Postfix(MinionStartingStats __instance, Vector3 location, ref GameObject __result)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession) return;
			if (!MultiplayerSession.IsHost) return;
			if (__result == null) return;

			try
			{
				// Get or add NetworkIdentity
				var identity = __result.AddOrGet<NetworkIdentity>();

				// Make sure the NetId is valid (not 0)
				if (identity.NetId == 0)
				{
					// Force registration to generate a new NetId
					identity.RegisterIdentity();
					DebugConsole.Log($"[MinionDeliverPatch] Registered with NetId {identity.NetId} for {__instance.Name}");
				}
				// Send EntitySpawnPacket to clients
				var packet = new TelepadEntitySpawnPacket
				{
					NetId = identity.NetId,
					Pos = location,
					EntityData = ImmigrantOptionEntry.FromGameDeliverable(__instance)
				};

				PacketSender.SendToAllClients(packet);
				DebugConsole.Log($"[MinionDeliverPatch] Sent EntitySpawnPacket for {__instance.Name} (NetId: {identity.NetId})");
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[MinionDeliverPatch] Error: {ex.Message}");
			}
		}
	}

	/// <summary>
	/// Patch CarePackageInfo.Deliver to send EntitySpawnPacket when care packages are spawned.
	/// </summary>
	[HarmonyPatch(typeof(CarePackageInfo), nameof(CarePackageInfo.Deliver))]
	public static class CarePackageDeliverPatch
	{
		public static void Postfix(CarePackageInfo __instance, Vector3 location, ref GameObject __result)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession) return;
			if (!MultiplayerSession.IsHost) return;
			if (__result == null) return;

			try
			{
				// Get or add NetworkIdentity
				var identity = __result.AddOrGet<NetworkIdentity>();

				// Make sure the NetId is valid (not 0)
				if (identity.NetId == 0)
				{
					identity.RegisterIdentity();
					DebugConsole.Log($"[CarePackageDeliverPatch] Registered with NetId {identity.NetId} for {__instance.id}");
				}

				// Send EntitySpawnPacket to clients
				var packet = new TelepadEntitySpawnPacket
				{
					NetId = identity.NetId,
					Pos = location,
					EntityData = ImmigrantOptionEntry.FromGameDeliverable(__instance)
				};

				PacketSender.SendToAllClients(packet);
				DebugConsole.Log($"[CarePackageDeliverPatch] Sent EntitySpawnPacket for {__instance.id} x{__instance.quantity} (NetId: {identity.NetId})");
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[CarePackageDeliverPatch] Error: {ex.Message}");
			}
		}
	}
}
