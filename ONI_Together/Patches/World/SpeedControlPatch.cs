using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.OxySync.Components;
using UnityEngine;

namespace ONI_Together.Patches.World
{
	[HarmonyPatch(typeof(SpeedControlScreen))]
	public static class SpeedControlPatch
	{
		private static readonly bool ENABLE_LOG = false;

		private static int _togglePauseDepth;

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
			if (!MultiplayerSession.InActiveSession || (MultiplayerSession.IsHost && !MultiplayerSession.SessionHasPlayers))
				return true;
			
			if (GameSpeedSyncer.Instance == null )
			{
				DebugConsole.LogWarning("[SpeedControlPatch][SetSpeed_Prefix] GameSpeedSyncer instance is null, allowing vanilla behavior.");
				return true;
			}
			
			if (GameSpeedSyncer.Instance.IsApplyingNetworkState)
			{
				if (ENABLE_LOG)
					DebugConsole.LogNonImportant("[SpeedControlPatch][SetSpeed_Prefix] GameSpeedSyncer instance is syncing, allowing vanilla behavior.");
				return true;
			}

			// Unpause() calls SetSpeed internally.
			// Let that vanilla SetSpeed happen, but don't create a separate network request.
			// TogglePause_Postfix will synchronize the final state.
			if (_togglePauseDepth > 0)
				return true;

			// The enum model cannot represent "paused with a selected resume speed".
			// Preserve vanilla behavior locally while paused. The chosen speed will
			// be synchronized when TogglePause actually resumes the game.
			if (SpeedControlScreen.Instance.IsPaused)
				return true;

			// Preserve vanilla SetSpeed's normalization.
			// The vanilla game would keep adding to the speed,
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

			var state = (GameSpeedSyncer.SpeedState)normalizedSpeed;

			// The vanilla game would proceed to set the speed even if it is already the current speed.
			// We intercept this to avoid unnecessary network requests, while still allowing the vanilla behavior to proceed.
			if (GameSpeedSyncer.Instance.IsStateSynchronized(state))
				return true;

			if (ENABLE_LOG)
				DebugConsole.LogNonImportant($"[SpeedControlPatch][SetSpeed_Prefix] Requesting speed {Speed} -> normalized {normalizedSpeed}.");

			GameSpeedSyncer.Instance.RequestSetSpeed(state);
			return false;
		}

		[HarmonyPrefix]
		[HarmonyPatch(nameof(SpeedControlScreen.TogglePause))]
		public static void TogglePause_Prefix()
		{
			_togglePauseDepth++;
		}

		[HarmonyPostfix]
		[HarmonyPatch(nameof(SpeedControlScreen.TogglePause))]
		public static void TogglePause_Postfix()
		{
			if (!MultiplayerSession.InActiveSession || (MultiplayerSession.IsHost && !MultiplayerSession.SessionHasPlayers))
				return;

			if (GameSpeedSyncer.Instance == null)
			{
				DebugConsole.LogWarning("[SpeedControlPatch][TogglePause_Postfix] GameSpeedSyncer instance is null.");
				return;
			}

			if (GameSpeedSyncer.Instance.IsApplyingNetworkState)
			{
				if (ENABLE_LOG)
					DebugConsole.LogNonImportant("[SpeedControlPatch][TogglePause_Postfix] GameSpeedSyncer instance is syncing.");
				return;
			}
			
			GameSpeedSyncer.SpeedState state = SpeedControlScreen.Instance.IsPaused
				? GameSpeedSyncer.SpeedState.Paused
				: (GameSpeedSyncer.SpeedState)SpeedControlScreen.Instance.GetSpeed();
			
			// Calling TogglePause would not always change the pause state in vanilla because of the internal counter.
			// Players would hit TogglePause multiple times if the 'Space' key not properly unpause the game.
			// It is annoying when a player joined the game and try to unpause, but repeatedly send the pause to everyone else.
			// When others trying to unpause at the same time,
			// plus arriving time and order of network packets are not guaranteed,
			// it make the experience frustrating for players as long as a new player joins the game.
			//
			// To mitigate this, we check if the desired state is already synchronized before sending a network request.
			if (GameSpeedSyncer.Instance.IsStateSynchronized(state))
				return;

			if (ENABLE_LOG)
				DebugConsole.LogNonImportant($"[SpeedControlPatch][TogglePause_Postfix] request setting speed to {state}.");

			GameSpeedSyncer.Instance.RequestSetSpeed(state);
		}

		[HarmonyFinalizer]
		[HarmonyPatch(nameof(SpeedControlScreen.TogglePause))]
		public static void TogglePause_Finalizer()
		{
			_togglePauseDepth = Mathf.Max(0, _togglePauseDepth - 1);
		}
	}
}
