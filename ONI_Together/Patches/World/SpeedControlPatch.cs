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

		// Set by the resume-gate prefixes when they block a call. Harmony still runs
		// postfixes after a prefix returns false, so the matching postfix reads this to
		// avoid broadcasting a change that never actually happened locally. (Re-checking
		// ResumeBlocked() in the postfix would be wrong for TogglePause: pausing while a
		// client is unready is allowed and must still be broadcast.)
		// Assumes non-reentrancy: SpeedControlScreen runs on the single game thread, so
		// prefix→original→postfix completes before another patched call begins. A blocked
		// call skips the original, so it can't trigger a nested SetSpeed/TogglePause.
		private static bool _resumeBlockedThisCall = false;

		// Authority gate: while in a session, the host must not resume/unpause the sim
		// until every connected player is ready. Pausing is always allowed; only resume
		// is blocked. IsSyncing lets remote-applied speed changes through.
		//
		// Coverage: this gate hooks the user-facing resume entry points — SetSpeed and
		// TogglePause (the play buttons / spacebar). SpeedControlScreen.Unpause() is NOT
		// patched. Nothing resumes via Unpause() today (the old auto-resume calls in
		// AllClientsReadyPacket / HardSyncCompletePacket / SaveLoaderPatch are commented
		// out), but any future code that resumes programmatically must route through this
		// gate (check ReadyManager.CanHostResume()) or add an Unpause prefix here —
		// otherwise it bypasses the safety.
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

		[HarmonyPatch("SetSpeed")]
		[HarmonyPostfix]
		public static void SetSpeed_Postfix(int Speed)
		{
			using var _ = Profiler.Scope();

			try
			{
				if (IsSyncing) return;

				// Prefix blocked this call — the local speed never changed, so don't
				// broadcast it or clients would resume while the host stays paused.
				if (_resumeBlockedThisCall) { _resumeBlockedThisCall = false; return; }

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

				// Prefix blocked this resume — don't broadcast; the local pause state
				// is unchanged. (Legitimate pauses are not blocked and still broadcast.)
				if (_resumeBlockedThisCall) { _resumeBlockedThisCall = false; return; }

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
