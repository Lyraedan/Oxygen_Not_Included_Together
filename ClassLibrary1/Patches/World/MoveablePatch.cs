using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using Shared.Profiling;
using UnityEngine;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.World
{
[HarmonyPatch(typeof(Movable), "OnSpawn")]
public static class MoveablePatch
{
	public static void Postfix(Movable __instance)
	{
		using var _ = Profiler.Scope();

		__instance.onPickupComplete += OnPickup;
		var properName = global::STRINGS.UI.StripLinkFormatting(__instance.gameObject.GetProperName());
		DebugConsole.Log($"[Movable.OnSpawn] Attached to: {properName} ({__instance.GetInstanceID()})");
	}
	static void OnPickup(GameObject go)
	{
		using var _ = Profiler.Scope();

		DebugConsole.Log($"[Movable.onPickupComplete] Picked up {go.name}");

		if (!MultiplayerSession.IsHost)
			return;

		if (go == null)
			return;

		if (!go.TryGetComponent<NetworkIdentity>(out var identity))
			return;

		DebugConsole.Log($"[Movable.onPickupComplete] Picked up NetID {identity.NetId}");

		// Optional: PacketSender.SendToAll(new DespawnPacket { NetId = identity.NetId });
	}
}

}