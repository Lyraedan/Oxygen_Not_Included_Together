using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.OxySync.Components;

namespace ONI_Together.Patches.World
{
	[HarmonyPatch(typeof(SpeedControlScreen))]
	public static class SpeedControlPatch
	{
		private static readonly bool ENABLE_LOG = false;

		[HarmonyPostfix]
		[HarmonyPatch(nameof(SpeedControlScreen.OnPrefabInit))]
		public static void OnPrefabInit_Postfix(SpeedControlScreen __instance)
		{
			if (ENABLE_LOG)
				DebugConsole.Log("[SpeedControlPatch][OnPrefabInit_Postfix] Adding GameSpeedSyncer component.");
			__instance?.gameObject.AddOrGet<GameSpeedSyncer>();
		}

		[HarmonyPrefix]
		[HarmonyPatch(nameof(SpeedControlScreen.SetSpeed))]
		public static bool SetSpeed_Prefix(int Speed)
		{
			if (!ShouldInterceptForSync())
				return true;

			SpeedControlScreen screen = SpeedControlScreen.Instance;

			// Preserve vanilla SetSpeed's normalization.
			// The vanilla game would keep adding to the speed, ex: Tab
			// so the Speed can be > 2 when SetSpeed is called.
			int normalizedSpeed = Speed % 3;

			// Do NOT accidentally turn SetSpeed(-1) into Paused.
			// Vanilla SetSpeed(-1) does not mean TogglePause().
			if (normalizedSpeed < 0)
			{
				if (ENABLE_LOG)
					DebugConsole.Log("[SpeedControlPatch][SetSpeed_Prefix] Normalized speed is negative, allowing vanilla behavior.");
				return true;
			}

			var state = (GameSpeedSyncer.SpeedMode)normalizedSpeed;

			// The vanilla game would proceed to set the speed even if it is already the current speed.
			// We intercept this to avoid unnecessary network requests, while still allowing the vanilla behavior to proceed.
			if (GameSpeedSyncer.Instance.IsStateSynchronized(state, screen.IsPaused))
				return true;

			if (ENABLE_LOG)
				DebugConsole.LogNonImportant($"[SpeedControlPatch][SetSpeed_Prefix] Requesting speed {Speed} -> normalized {normalizedSpeed}.");

			GameSpeedSyncer.Instance.RequestSetSpeed(state, screen.IsPaused);
			return false;
		}

		[HarmonyPrefix]
		[HarmonyPatch(nameof(SpeedControlScreen.TogglePause))]
		public static bool TogglePause_Prefix()
		{
			if (!ShouldInterceptForSync())
				return true;
			
			SpeedControlScreen screen = SpeedControlScreen.Instance;
			bool isPaused = screen.IsPaused;
			GameSpeedSyncer.SpeedMode state = (GameSpeedSyncer.SpeedMode)screen.GetSpeed();


			if (ENABLE_LOG)
				DebugConsole.LogNonImportant($"[SpeedControlPatch][TogglePause_Prefix] request setting speed to {state}, isPaused: {!isPaused}.");

			GameSpeedSyncer.Instance.RequestSetSpeed(state, !isPaused);
			return false;
		}

		public static bool ShouldInterceptForSync() {
			if (!MultiplayerSession.InActiveSession || (MultiplayerSession.IsHost && !MultiplayerSession.SessionHasPlayers))
				return false;

			if (GameSpeedSyncer.Instance == null)
			{
				DebugConsole.LogWarning("[SpeedControlPatch][ShouldInterceptForSync] GameSpeedSyncer instance is null.");
				return false;
			}

			if (GameSpeedSyncer.Instance.IsApplyingNetworkState)
			{
				if (ENABLE_LOG)
					DebugConsole.LogNonImportant("[SpeedControlPatch][ShouldInterceptForSync] GameSpeedSyncer instance is syncing.");
				return false;
			}

			return true;
		}
	}
}
