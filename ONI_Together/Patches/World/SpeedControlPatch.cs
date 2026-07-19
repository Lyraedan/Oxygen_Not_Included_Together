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

		private static bool AllowLocalSpeedMutation()
			=> !SpeedChangePacket.ShouldBlockLocalSpeedControl(
				MultiplayerSession.InSession,
				IsSyncing,
				SpeedChangePacket.IsBarrierPauseLocked);

		[HarmonyPatch("SetSpeed")]
		[HarmonyPrefix]
		public static bool SetSpeed_Prefix() => AllowLocalSpeedMutation();

		[HarmonyPatch("SetSpeed")]
		[HarmonyPostfix]
		public static void SetSpeed_Postfix(int Speed)
		{
			using var _ = Profiler.Scope();

			try
			{
				if (IsSyncing || !AllowLocalSpeedMutation()) return;
				ReadyManager.ClearAutomaticPauseOwnership();

				var speed = (SpeedChangePacket.SpeedState)Speed;
				SpeedChangePacket.SubmitLocalChange(speed);
				DebugConsole.Log($"[SpeedControl] Submitted speed change: {speed}");
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[SpeedControlPatch.SetSpeed_Postfix] {ex}");
			}
		}

		[HarmonyPatch(nameof(SpeedControlScreen.TogglePause))]
		[HarmonyPrefix]
		public static bool TogglePause_Prefix() => AllowLocalSpeedMutation();

		[HarmonyPatch(nameof(SpeedControlScreen.TogglePause))]
		[HarmonyPostfix]
		public static void TogglePause_Postfix(SpeedControlScreen __instance)
		{
			using var _ = Profiler.Scope();

			try
			{
				if (IsSyncing || !AllowLocalSpeedMutation()) return;
				ReadyManager.ClearAutomaticPauseOwnership();

				var speedState = __instance.IsPaused
						? SpeedChangePacket.SpeedState.Paused
						: (SpeedChangePacket.SpeedState)__instance.GetSpeed();

				SpeedChangePacket.SubmitLocalChange(speedState);
				DebugConsole.Log($"[SpeedControl] Submitted pause change: {speedState}");
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[SpeedControlPatch.TogglePause_Postfix] {ex}");
			}
		}

		[HarmonyPatch(nameof(SpeedControlScreen.Unpause))]
		[HarmonyPrefix]
		public static bool Unpause_Prefix() => AllowLocalSpeedMutation();
	}
}
