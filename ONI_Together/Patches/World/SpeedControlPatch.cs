using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.OxySync.Components;
using System;
using Shared.Profiling;

namespace ONI_Together.Patches
{
	[HarmonyPatch(typeof(SpeedControlScreen))]
	public static class SpeedControlScreen_SendSpeedPacketPatch
	{
		public static bool IsSyncing = false;

		// Set by the resume-gate prefixes when they block a call: Harmony still runs the
		// postfix after a prefix returns false. The postfix must read this rather than
		// re-check ResumeBlocked(), which would also suppress a legitimate pause broadcast.
		// Safe as one field: these calls run on the game thread and cannot nest.
		private static bool _resumeBlockedThisCall = false;

		// HOST ONLY - in a session the sim must not resume until every player is ready;
		// pausing is always allowed, and IsSyncing lets remote-applied changes through.
		// All three local resume entry points are hooked (SetSpeed, TogglePause, Unpause);
		// client-originated resumes are rejected in GameSpeedSyncer.CmdSetSpeed.
		private static bool ResumeBlocked()
		{
			if (IsSyncing) return false;
			if (!MultiplayerSession.IsHost || !MultiplayerSession.InActiveSession) return false;
			return !ReadyManager.CanHostResume();
		}

		[HarmonyPatch("OnPrefabInit")]
		[HarmonyPostfix]
		public static void OnPrefabInit_Postfix(SpeedControlScreen __instance)
		{
			if (!__instance.TryGetComponent<GameSpeedSyncer>(out _))
				__instance.gameObject.AddComponent<GameSpeedSyncer>();
		}

		[HarmonyPatch("SetSpeed")]
		[HarmonyPrefix]
		public static bool SetSpeed_Prefix()
		{
			using var _ = Profiler.Scope();

			// Setting a speed unpauses the sim — block it while players are not ready.
			if (ResumeBlocked())
			{
				_resumeBlockedThisCall = true;
				DebugConsole.Log("[SpeedControl] Blocked SetSpeed: not all players are ready");
				ReadyManager.RefreshScreen();
				return false;
			}
			_resumeBlockedThisCall = false;
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
				_resumeBlockedThisCall = true;
				DebugConsole.Log("[SpeedControl] Blocked TogglePause (resume): not all players are ready");
				ReadyManager.RefreshScreen();
				return false;
			}
			_resumeBlockedThisCall = false;
			return true;
		}

		[HarmonyPatch(nameof(SpeedControlScreen.Unpause), new Type[] { typeof(bool) })]
		[HarmonyPrefix]
		public static bool Unpause_Prefix()
		{
			using var _ = Profiler.Scope();

			// Direct/programmatic Unpause() must obey the gate too. No flag bookkeeping
			// needed: Unpause has no broadcasting postfix here.
			if (ResumeBlocked())
			{
				DebugConsole.Log("[SpeedControl] Blocked Unpause: not all players are ready");
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
				if (!MultiplayerSession.InActiveSession) return;

				// Blocked by the prefix: the local speed never changed, so a request here
				// would resume clients while the host stays paused.
				if (_resumeBlockedThisCall) { _resumeBlockedThisCall = false; return; }

				GameSpeedSyncer.Instance?.RequestSetSpeed(Speed);
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[SpeedControlPatch.SetSpeed_Postfix] {ex}");
			}
		}

		[HarmonyPatch(nameof(SpeedControlScreen.TogglePause))]
		[HarmonyPostfix]
		public static void TogglePause_Postfix()
		{
			using var _ = Profiler.Scope();

			try
			{
				if (IsSyncing) return;
				if (!MultiplayerSession.InActiveSession) return;

				// Blocked by the prefix: pause state unchanged. (Real pauses still broadcast.)
				if (_resumeBlockedThisCall) { _resumeBlockedThisCall = false; return; }

				// The original already ran, so IsPaused is the post-toggle state.
				var newState = SpeedControlScreen.Instance.IsPaused
					? (int)GameSpeedSyncer.SpeedState.Paused
					: SpeedControlScreen.Instance.GetSpeed();

				GameSpeedSyncer.Instance?.RequestSetSpeed(newState);
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[SpeedControlPatch.TogglePause_Postfix] {ex}");
			}
		}
	}
}
