using ONI_Together.Networking.Transport;

namespace ONI_Together.DebugTools.UnitTests
{
	/// <summary>
	/// Covers the load-in-flight bookkeeping on TransportServer, which the resume gate and
	/// the ready-screen total both read. Deliberately side-effect free: the ids used here are
	/// picked so they can never be in MultiplayerSession.ConnectedPlayers, so the tests are
	/// safe to run mid-session and never touch shared state.
	/// </summary>
	public static class TransportLoadTrackingTests
	{
		// Off-roster by construction - no real transport hands out ids this high.
		private const ulong FakeLoaderA = ulong.MaxValue - 7;
		private const ulong FakeLoaderB = ulong.MaxValue - 8;

		/// <summary>Minimal concrete transport so the shared bookkeeping can be exercised.</summary>
		private sealed class ProbeServer : TransportServer
		{
			public override void Prepare() { }
			public override void Start() { }
			public override void Stop() { }
			public override void CloseConnections() { }
			public override void Update() { }
			public override void OnMessageRecieved() { }
			public override void KickClient(ulong clientId) { }
		}

		[UnitTest(name: "Transport: off-roster loader holds the resume gate", category: "Transport")]
		public static UnitTestResult OffRosterLoaderIsCounted()
		{
			var server = new ProbeServer();

			if (server.PendingLoadingClientCount != 0 || server.HasPendingLoadingClients)
				return UnitTestResult.Fail("A fresh transport reported a pending load");

			server.MarkClientLoading(FakeLoaderA);

			if (!server.IsClientLoading(FakeLoaderA))
				return UnitTestResult.Fail("MarkClientLoading did not record the loader");

			if (server.PendingLoadingClientCount != 1)
				return UnitTestResult.Fail(
					$"Expected 1 off-roster loader, got {server.PendingLoadingClientCount}");

			if (!server.HasPendingLoadingClients)
				return UnitTestResult.Fail("HasPendingLoadingClients disagreed with the count");

			return UnitTestResult.Pass("An off-roster loader is counted and holds the gate");
		}

		[UnitTest(name: "Transport: reconnect by exact id clears the load", category: "Transport")]
		public static UnitTestResult ExactIdReconnectClearsLoad()
		{
			var server = new ProbeServer();
			server.MarkClientLoading(FakeLoaderA);

			// LiteNetLib and Steamworks give a returning client its persistent id back.
			server.ClaimLoadingReconnect(FakeLoaderA);

			if (server.PendingLoadingClientCount != 0)
				return UnitTestResult.Fail("The load entry survived a matching reconnect");

			if (!server.ConsumeReconnectFromLoad(FakeLoaderA))
				return UnitTestResult.Fail("The reconnect was not reported as a returning loader");

			if (server.ConsumeReconnectFromLoad(FakeLoaderA))
				return UnitTestResult.Fail("The returning-loader flag was not consumed once");

			return UnitTestResult.Pass("An exact-id reconnect clears the load and reports once");
		}

		[UnitTest(name: "Transport: a kicked loader stops holding the gate", category: "Transport")]
		public static UnitTestResult ForgetClientLoadingReleasesTheGate()
		{
			var server = new ProbeServer();
			server.MarkClientLoading(FakeLoaderA);

			if (server.PendingLoadingClientCount != 1)
				return UnitTestResult.Fail("Setup failed - the loader was not counted");

			// A kicked client is not coming back. Its own disconnect event cannot clear the
			// entry (the kick already removed the peer mapping that event is keyed on), so
			// without this the gate stays closed until the 120s timeout.
			server.ForgetClientLoading(FakeLoaderA);

			if (server.PendingLoadingClientCount != 0)
				return UnitTestResult.Fail("A forgotten loader still holds the gate");

			if (server.ConsumeReconnectFromLoad(FakeLoaderA))
				return UnitTestResult.Fail("A forgotten loader was credited as a returning loader");

			return UnitTestResult.Pass("ForgetClientLoading releases the gate without crediting a reconnect");
		}

		[UnitTest(name: "Transport: a new join does not clear someone else's load", category: "Transport")]
		public static UnitTestResult UnmatchedConnectLeavesLoadPending()
		{
			var server = new ProbeServer();
			server.MarkClientLoading(FakeLoaderA);

			// A different client connecting is a new join, not the loader coming back. If this
			// consumed A's entry the gate would rest only on B's Unready flag and would open
			// the moment B reported Ready - with A still mid-load, which is the exact bug the
			// pending count exists to prevent.
			server.ClaimLoadingReconnect(FakeLoaderB);

			if (server.PendingLoadingClientCount != 1)
				return UnitTestResult.Fail(
					$"A new join consumed the pending load (count {server.PendingLoadingClientCount})");

			if (!server.IsClientLoading(FakeLoaderA))
				return UnitTestResult.Fail("The original loader's entry was dropped");

			if (server.ConsumeReconnectFromLoad(FakeLoaderB))
				return UnitTestResult.Fail("A new join was mis-tagged as a returning loader");

			return UnitTestResult.Pass("An unmatched connect leaves the pending load and the gate alone");
		}
	}
}
