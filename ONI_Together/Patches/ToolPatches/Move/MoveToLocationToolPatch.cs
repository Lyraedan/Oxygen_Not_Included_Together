using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Tools.Move;
using Shared.Profiling;

namespace ONI_Together.Patches.ToolPatches.Move
{
	[HarmonyPatch(typeof(MoveToLocationTool), "SetMoveToLocation")]
	public static class MoveToLocationToolPatch
	{
		public static bool Prefix(MoveToLocationTool __instance, int target_cell)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession || MultiplayerSession.IsHost) return true; // Run like normal

			var nav = __instance.targetNavigator;
			var movable = __instance.targetMovable;
			var go = nav?.gameObject ?? movable?.gameObject;

			if (go == null || !go.TryGetComponent<NetworkIdentity>(out var identity))
			{
				DebugConsole.LogWarning("[Client] Cannot send move request: no NetworkIdentity.");
				return false;
			}

			// Send move request to host
			var packet = new MoveToLocationPacket
			{
				Cell = target_cell,
				TargetNetId = identity.NetId
			};

			PacketSender.SendToHost(packet);
			DebugConsole.Log($"[Client] Sent MoveToLocationPacket to host for NetId {identity.NetId} to move to {target_cell}");

			return false; // Block normal executing on clients
		}
	}
}
