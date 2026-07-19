using System;
using ONI_Together.Networking;

namespace ONI_Together.DebugTools.UnitTests;

public static class ReconnectProofLedgerTests
{
	[UnitTest(name: "Completed Ready proof is idempotent until exact ack", category: "Sync")]
	public static UnitTestResult CompletedProofIsIdempotentUntilAck()
	{
		var ledger = new CompletedReadyProofLedger();
		var now = new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
		if (!ledger.Record(10, 77, 5, now)
		    || !ledger.Record(10, 77, 5, now.AddSeconds(1))
		    || ledger.Count != 1
		    || ledger.Record(11, 77, 5, now)
		    || ledger.Record(10, 77, 6, now))
			return UnitTestResult.Fail("Completed proof was not idempotent and exact");
		if (ledger.Acknowledge(11, 77, 5)
		    || ledger.Acknowledge(10, 77, 6)
		    || !ledger.Acknowledge(10, 77, 5)
		    || ledger.Count != 0)
			return UnitTestResult.Fail("Stale or forged Ready acknowledgement changed the ledger");

		return UnitTestResult.Pass("Completed Ready is replayable until the exact client acknowledgement");
	}

	[UnitTest(name: "Completed LAN proof authenticates fallback until lease expiry", category: "Sync")]
	public static UnitTestResult CompletedProofLeaseAndIdentityPolicy()
	{
		var ledger = new CompletedReadyProofLedger();
		var now = new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
		ledger.Record(10, 88, 6, now);
		if (!ledger.AuthorizesReconnect(20, 88, requireSameClient: false)
		    || ledger.AuthorizesReconnect(20, 88, requireSameClient: true)
		    || !ledger.AuthorizesReconnect(10, 88, requireSameClient: true))
			return UnitTestResult.Fail("Completed proof confused LAN bearer and Steam identity policy");
		if (ledger.Prune(now.AddSeconds(299), TimeSpan.FromSeconds(300)) != 0
		    || ledger.Prune(now.AddSeconds(301), TimeSpan.FromSeconds(300)) != 1
		    || ledger.Count != 0)
			return UnitTestResult.Fail("Completed proof did not obey the bounded recovery lease");

		return UnitTestResult.Pass("Completed proof authorizes bounded LAN recovery without weakening Steam identity");
	}
}
