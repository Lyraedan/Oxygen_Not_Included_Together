using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;
using System;
using Shared.Profiling;

namespace ONI_Together.Patches
{
	[HarmonyPatch(typeof(SpeedControlScreen))]
	public static class SpeedControlScreen_SendSpeedPacketPatch
	{
		public static bool IsSyncing = false;

		// Authority gate: while in a session, the host must not resume/unpause the sim
		// until every connected player is ready. Pausing is always allowed; only resume
		// is blocked. IsSyncing lets remote-applied speed changes through.
		private static bool ResumeBlocked()
		{
			if (IsSyncing) return false;
			if (!MultiplayerSession.IsHost || !MultiplayerSession.InSession) return false;
			return !ReadyManager.CanHostResume();
		}

		[HarmonyPatch("SetSpeed")]
		[HarmonyPrefix]
		public static bool SetSpeed_Prefix()
		{
			using var _ = Profiler.Scope();

			// Setting a speed unpauses the sim — block it while players are not ready.
			if (ResumeBlocked())
			{
				DebugConsole.Log("[SpeedControl] Blocked SetSpeed: not all players are ready");
				ReadyManager.RefreshScreen();
				return false;
			}
			return true;
		}

		[HarmonyPatch(nameof(SpeedControlScreen.TogglePause))]
		[HarmonyPrefix]
		public static bool TogglePause_Prefix(SpeedControlScreen __instance)
		{
			using var _ = Profiler.Scope();

			// TogglePause only resumes when currently paused; pausing stays allowed.
			if (__instance.IsPaused && ResumeBlocked())
			{
				DebugConsole.Log("[SpeedControl] Blocked TogglePause (resume): not all players are ready");
				ReadyManager.RefreshScreen();
				return false;
			}
			return true;
		}

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

		[HarmonyPatch(nameof(SpeedControlScreen.TogglePause))]
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
