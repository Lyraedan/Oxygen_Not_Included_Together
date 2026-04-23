using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.World;
using Shared.Profiling;
using UnityEngine;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.World
{
	// Pickupable.OnCleanUp only fires when the object is destroyed. Items that are
	// reparented into Storage (seeds into planters, eggs into incubators, live
	// critters, non-stackable items) stay alive and never trigger OnCleanUp, so
	// clients keep rendering them on the ground. Mirror the pickup on Store() so
	// the existing GroundItemPickedUpPacket path removes the client-side ghost.
	[HarmonyPatch(typeof(Storage), nameof(Storage.Store))]
	public static class StorageStorePatch
	{
		public static void Postfix(GameObject go)
		{
			using var _ = Profiler.Scope();
			try
			{
				if (!MultiplayerSession.IsHost || !MultiplayerSession.InSession)
					return;
				if (go == null)
					return;

				var identity = go.GetComponent<NetworkIdentity>();
				if (identity == null || identity.NetId == 0)
					return;

				PacketSender.SendToAllClients(new GroundItemPickedUpPacket { NetId = identity.NetId });
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[StorageStorePatch] Exception: {ex}");
			}
		}
	}
}
