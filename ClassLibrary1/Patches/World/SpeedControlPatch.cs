using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.World;
using System;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.World
{
	[HarmonyPatch(typeof(SpeedControlScreen))]
	public static class SpeedControlScreen_SendSpeedPacketPatch
	{
		public static bool IsSyncing = false;

		[HarmonyPatch("SetSpeed")]
		[HarmonyPostfix]
		public static void SetSpeed_Postfix(int Speed)
		{
			using var _ = Profiler.Scope();

			try
			{
				if (IsSyncing) return;

				var packet = new SpeedChangePacket((SpeedChangePacket.SpeedState)Speed);

				PacketSender.SendToAllOtherPeers(packet);
				DebugConsole.Log($"[SpeedControl] Sent SpeedChangePacket: {packet.Speed}");
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[SpeedControlPatch.SetSpeed_Postfix] {ex}");
			}
		}

		[HarmonyPatch("TogglePause")]
		[HarmonyPostfix]
		public static void TogglePause_Postfix(SpeedControlScreen __instance)
		{
			using var _ = Profiler.Scope();

			try
			{
				if (IsSyncing) return;

				var speedState = __instance.IsPaused
						? SpeedChangePacket.SpeedState.Paused
						: (SpeedChangePacket.SpeedState)__instance.GetSpeed();

				var packet = new SpeedChangePacket(speedState);
				PacketSender.SendToAllOtherPeers(packet);
				DebugConsole.Log($"[SpeedControl] Sent SpeedChangePacket (pause toggle): {packet.Speed}");
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[SpeedControlPatch.TogglePause_Postfix] {ex}");
			}
		}
	}
}
