#if DEBUG
using System;
using System.IO;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class ProductionDesyncRecoveryTests
	{
		[UnitTest(name: "Production desync hash reports five independent domains", category: "Networking")]
		public static UnitTestResult HashReportsIndependentDomains()
		{
			ProductionStateHashes expected = Hashes(1);
			ProductionStateHashes actual = Hashes(1);
			actual.Storage[0] = 9;
			actual.ClusterRocket[0] = 8;

			ProductionDesyncDomain mismatch = expected.DifferentDomains(actual);
			return mismatch == (ProductionDesyncDomain.Storage | ProductionDesyncDomain.ClusterRocket)
				? UnitTestResult.Pass("Storage and cluster/rocket mismatches remain independently visible")
				: UnitTestResult.Fail($"Unexpected production mismatch mask: {mismatch}");
		}

		[UnitTest(name: "Production desync recovery retries only a grid mismatch locally", category: "Networking")]
		public static UnitTestResult RecoveryEscalationIsBounded()
		{
			bool valid = ProductionDesyncRecovery.SelectRecoveryAction(
				ProductionDesyncDomain.None, localRepairAttempted: false)
				== ProductionRecoveryAction.Release
				&& ProductionDesyncRecovery.SelectRecoveryAction(
					ProductionDesyncDomain.Grid, localRepairAttempted: false)
				== ProductionRecoveryAction.GridRepair
				&& ProductionDesyncRecovery.SelectRecoveryAction(
					ProductionDesyncDomain.Grid, localRepairAttempted: true)
				== ProductionRecoveryAction.HardSync
				&& ProductionDesyncRecovery.SelectRecoveryAction(
					ProductionDesyncDomain.EntityLifecycle, localRepairAttempted: false)
				== ProductionRecoveryAction.HardSync;
			return valid
				? UnitTestResult.Pass("Only one pure-grid authoritative repair precedes hard sync")
				: UnitTestResult.Fail("Production recovery could loop or apply an unsafe local repair");
		}

		[UnitTest(name: "Production desync report preserves every domain hash", category: "Networking")]
		public static UnitTestResult ReportRoundTripPreservesDomains()
		{
			var source = new ProductionDesyncReportPacket
			{
				ProbeId = 7,
				RepairSequenceCut = 13,
				Hashes = Hashes(3),
			};
			ProductionDesyncReportPacket received = RoundTrip(source);
			return received.ProbeId == 7 && received.RepairSequenceCut == 13
			       && received.Hashes.DifferentDomains(source.Hashes) == ProductionDesyncDomain.None
				? UnitTestResult.Pass("Production report carries five canonical hashes")
				: UnitTestResult.Fail("Production report lost a marker or domain hash");
		}

		[UnitTest(name: "Production checkpoint yields to the debug soak probe", category: "Networking")]
		public static UnitTestResult DebugProbeOwnsCheckpointBarrier()
		{
			return !ProductionDesyncRecovery.CanStartAgainstDebugProbe(debugProbeRunning: true)
			       && ProductionDesyncRecovery.CanStartAgainstDebugProbe(debugProbeRunning: false)
				? UnitTestResult.Pass("Production and debug checkpoints cannot own the barrier together")
				: UnitTestResult.Fail("Production checkpoint can race the debug soak barrier");
		}

		[UnitTest(name: "Production storage hash excludes identities without storage", category: "Networking")]
		public static UnitTestResult StorageDomainExcludesEmptyIdentities()
		{
			return !ProductionStateHash.ShouldHashStorageOwner(0)
			       && ProductionStateHash.ShouldHashStorageOwner(1)
				? UnitTestResult.Pass("Lifecycle-only identities cannot pollute the storage hash")
				: UnitTestResult.Fail("Storage hash includes an identity without Storage components");
		}

		private static ProductionDesyncReportPacket RoundTrip(ProductionDesyncReportPacket source)
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				source.Serialize(writer);
			stream.Position = 0;
			var received = new ProductionDesyncReportPacket();
			using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
				received.Deserialize(reader);
			return received;
		}

		private static ProductionStateHashes Hashes(byte seed)
		{
			byte[] Hash(byte value)
			{
				var hash = new byte[ProductionStateHashes.HashLength];
				Array.Fill(hash, value);
				return hash;
			}

			return new ProductionStateHashes
			{
				Grid = Hash(seed),
				EntityLifecycle = Hash((byte)(seed + 1)),
				WorldMembership = Hash((byte)(seed + 2)),
				Storage = Hash((byte)(seed + 3)),
				ClusterRocket = Hash((byte)(seed + 4)),
			};
		}
	}
}
#endif
