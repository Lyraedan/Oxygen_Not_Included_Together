using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class StorageRevisionGateTests
	{
		[UnitTest(name: "Storage revision batch validates before commit", category: "Sync")]
		public static UnitTestResult StorageRevisionBatchIsAllOrNothing()
		{
			var revisions = new System.Collections.Generic.Dictionary<int, ulong>
			{
				[-1_000_001] = 100,
				[0] = 101,
			};
			if (NetworkIdentityRegistry.TryCommitStorageSnapshotRevisions(revisions)
			    || NetworkIdentityRegistry.GetLastStorageSnapshotRevision(-1_000_001) != 0)
				return UnitTestResult.Fail("Storage revision batch partially committed");
			return UnitTestResult.Pass("Storage revisions commit only after every target validates");
		}

		[UnitTest(name: "Composite storage snapshots advance state and transfer cut", category: "Sync")]
		public static UnitTestResult CompositeSnapshotRevisionGate()
		{
			if (!NetworkIdentityRegistry.ShouldAcceptStateAndStorageSnapshotRevision(
				    stateCurrent: 7, snapshotCurrent: 8, eventCurrent: 9,
				    lifecycleCurrent: 10, incoming: 11))
			{
				return UnitTestResult.Fail("New composite storage snapshot was rejected");
			}
			if (NetworkIdentityRegistry.ShouldAcceptStateAndStorageSnapshotRevision(
				    stateCurrent: 11, snapshotCurrent: 0, eventCurrent: 0,
				    lifecycleCurrent: 0, incoming: 11) ||
			    NetworkIdentityRegistry.ShouldAcceptStateAndStorageSnapshotRevision(
				    stateCurrent: 0, snapshotCurrent: 11, eventCurrent: 0,
				    lifecycleCurrent: 0, incoming: 11) ||
			    NetworkIdentityRegistry.ShouldAcceptStateAndStorageSnapshotRevision(
				    stateCurrent: 0, snapshotCurrent: 0, eventCurrent: 12,
				    lifecycleCurrent: 0, incoming: 11) ||
			    NetworkIdentityRegistry.ShouldAcceptStateAndStorageSnapshotRevision(
				    stateCurrent: 0, snapshotCurrent: 0, eventCurrent: 0,
				    lifecycleCurrent: 12, incoming: 11))
			{
				return UnitTestResult.Fail("Stale composite storage snapshot crossed an existing cut");
			}
			if (StorageItemPacket.ShouldApplyRevision(
				    lastSnapshotRevision: 11, lastItemRevision: 0,
				    lastItemLifecycleRevision: 0, lastStorageLifecycleRevision: 0,
				    incomingRevision: 10))
			{
				return UnitTestResult.Fail("Transfer older than composite snapshot cut was accepted");
			}
			return UnitTestResult.Pass("Toilet and reactor snapshots reject delayed storage intent");
		}
	}
}
