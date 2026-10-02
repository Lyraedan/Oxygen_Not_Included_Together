using ONI_Together.Patches;
using ONI_Together.Networking;
using ONI_Together.Networking.Transport;

namespace ONI_Together.DebugTools.UnitTests
{
	/// <summary>
	/// Drives the host-side resume gate without a second machine: a real load window lasts
	/// seconds and cannot be timed by hand, so these tests fabricate the pending load
	/// instead of waiting for one.
	///
	/// HOST ONLY, and they move the sim: each test records the speed and pause state up
	/// front and restores it in a finally block, and the fabricated loader is always removed.
	/// </summary>
	public static class ResumeGateTests
	{
		// Off-roster by construction - no transport hands out ids this high, so the probe is
		// counted as a pending load without touching MultiplayerSession.ConnectedPlayers.
		private const ulong ProbeLoader = ulong.MaxValue - 11;

		private static UnitTestResult Preconditions(out TransportServer server)
		{
			server = NetworkConfig.TransportServer;

			// Not applicable is not a failure. Reported as a pass carrying "skipped" until the
			// harness grows a real skipped state.
			if (!MultiplayerSession.InActiveSession || !MultiplayerSession.IsHost)
				return UnitTestResult.Pass("skipped: host only");
			if (server == null)
				return UnitTestResult.Pass("skipped: no transport server");
			if (SpeedControlScreen.Instance == null)
				return UnitTestResult.Pass("skipped: SpeedControlScreen not available yet");
			if (!ReadyManager.CanHostResume())
				return UnitTestResult.Pass("skipped: the gate is already closed");

			return null;
		}

		private static void ClearProbe(TransportServer server)
		{
			// Same path a returning loader takes: the exact-id match drops the entry, then the
			// returning-loader flag it sets is consumed so nothing is left behind.
			server.ClaimLoadingReconnect(ProbeLoader);
			server.ConsumeReconnectFromLoad(ProbeLoader);

			// Put the screen back. Each blocked resume calls RefreshScreen, which renders the
			// fabricated loader into the overlay ("0/1 ready"), and nothing recomputes it
			// afterwards - so without this the host is left sitting behind a stale "waiting for
			// players" overlay once the tests finish.
			ReadyManager.RefreshScreen();
			ReadyManager.RefreshReadyState();
		}

		[UnitTest(name: "Resume gate: a pending load closes the gate", category: "Sync")]
		public static UnitTestResult PendingLoadClosesGate()
		{
			var pre = Preconditions(out var server);
			if (pre != null) return pre;

			try
			{
				int before = server.PendingLoadingClientCount;
				server.MarkClientLoading(ProbeLoader);

				if (server.PendingLoadingClientCount != before + 1)
					return UnitTestResult.Fail("The fabricated loader was not counted as pending");

				if (ReadyManager.CanHostResume())
					return UnitTestResult.Fail("CanHostResume stayed true while a load was in flight");

				return UnitTestResult.Pass("A pending load closes the resume gate");
			}
			finally
			{
				ClearProbe(server);
			}
		}

		[UnitTest(name: "Resume gate: host cannot resume mid-load", category: "Sync")]
		public static UnitTestResult HostResumeIsBlockedMidLoad()
		{
			var pre = Preconditions(out var server);
			if (pre != null) return pre;

			bool wasPaused = SpeedControlScreen.Instance.IsPaused;
			int previousSpeed = SpeedControlScreen.Instance.GetSpeed();

			try
			{
				// A resume is only observable from a paused sim.
				if (!SpeedControlScreen.Instance.IsPaused)
					SpeedControlScreen.Instance.TogglePause(false);

				server.MarkClientLoading(ProbeLoader);

				// Each of the three local resume entry points the gate patches.
				SpeedControlScreen.Instance.Unpause(false);
				if (!SpeedControlScreen.Instance.IsPaused)
					return UnitTestResult.Fail("Unpause() resumed the sim while a load was in flight");

				SpeedControlScreen.Instance.TogglePause(false);
				if (!SpeedControlScreen.Instance.IsPaused)
					return UnitTestResult.Fail("TogglePause() resumed the sim while a load was in flight");

				SpeedControlScreen.Instance.SetSpeed(2);
				if (!SpeedControlScreen.Instance.IsPaused)
					return UnitTestResult.Fail("SetSpeed() resumed the sim while a load was in flight");

				return UnitTestResult.Pass("SetSpeed, TogglePause and Unpause are all blocked mid-load");
			}
			finally
			{
				ClearProbe(server);

				// Restore what the test moved. The gate is open again by now, so these go
				// through; IsSyncing keeps the restore from being treated as a local request.
				SpeedControlScreen_SendSpeedPacketPatch.IsSyncing = true;
				try
				{
					if (!wasPaused && SpeedControlScreen.Instance.IsPaused)
					{
						SpeedControlScreen.Instance.TogglePause(false);
						SpeedControlScreen.Instance.SetSpeed(previousSpeed);
					}
					else if (wasPaused && !SpeedControlScreen.Instance.IsPaused)
					{
						SpeedControlScreen.Instance.TogglePause(false);
					}
				}
				finally
				{
					SpeedControlScreen_SendSpeedPacketPatch.IsSyncing = false;
				}
			}
		}

		[UnitTest(name: "Resume gate: pausing is never blocked", category: "Sync")]
		public static UnitTestResult PausingStaysAllowedMidLoad()
		{
			var pre = Preconditions(out var server);
			if (pre != null) return pre;

			bool wasPaused = SpeedControlScreen.Instance.IsPaused;

			try
			{
				// Start from a running sim so the pause is observable.
				if (SpeedControlScreen.Instance.IsPaused)
					SpeedControlScreen.Instance.TogglePause(false);

				if (SpeedControlScreen.Instance.IsPaused)
				{
					// A gate refusal always logs "[SpeedControl] Blocked ..."; ONI declining the
					// unpause for its own reasons logs nothing and is not this test's subject.
					return ReadyManager.CanHostResume()
						? UnitTestResult.Pass("skipped: ONI declined the setup unpause while the gate was OPEN")
						: UnitTestResult.Fail("The gate closed between the precondition check and the setup resume");
				}

				server.MarkClientLoading(ProbeLoader);

				SpeedControlScreen.Instance.TogglePause(false);
				if (!SpeedControlScreen.Instance.IsPaused)
					return UnitTestResult.Fail("Pausing was blocked - only resume may be gated");

				return UnitTestResult.Pass("Pausing still works while a load is in flight");
			}
			finally
			{
				ClearProbe(server);

				SpeedControlScreen_SendSpeedPacketPatch.IsSyncing = true;
				try
				{
					if (!wasPaused && SpeedControlScreen.Instance.IsPaused)
						SpeedControlScreen.Instance.TogglePause(false);
				}
				finally
				{
					SpeedControlScreen_SendSpeedPacketPatch.IsSyncing = false;
				}
			}
		}
	}
}
