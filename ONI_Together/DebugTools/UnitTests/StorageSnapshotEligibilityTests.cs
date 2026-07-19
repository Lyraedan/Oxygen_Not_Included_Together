#if DEBUG
using ONI_Together.Misc;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class StorageSnapshotEligibilityTests
	{
		[UnitTest(name: "Storage snapshot eligibility is canonical", category: "Networking")]
		public static UnitTestResult EligibilityRejectsUnsupportedState()
		{
			bool valid = StorageSnapshotSync.IsSnapshotStateEligible(
				itemExists: true, hasPrimaryElement: true, mass: 1f,
				temperature: 295f, diseaseCount: 0, prefabHash: 17);
			bool rejected = !StorageSnapshotSync.IsSnapshotStateEligible(
				false, true, 1f, 295f, 0, 17)
				&& !StorageSnapshotSync.IsSnapshotStateEligible(true, false, 1f, 295f, 0, 17)
				&& !StorageSnapshotSync.IsSnapshotStateEligible(true, true, 0f, 295f, 0, 17)
				&& !StorageSnapshotSync.IsSnapshotStateEligible(true, true, -1f, 295f, 0, 17)
				&& !StorageSnapshotSync.IsSnapshotStateEligible(true, true, float.NaN, 295f, 0, 17)
				&& !StorageSnapshotSync.IsSnapshotStateEligible(true, true, float.PositiveInfinity, 295f, 0, 17)
				&& !StorageSnapshotSync.IsSnapshotStateEligible(true, true, 1f, -1f, 0, 17)
				&& !StorageSnapshotSync.IsSnapshotStateEligible(true, true, 1f, float.NaN, 0, 17)
				&& !StorageSnapshotSync.IsSnapshotStateEligible(true, true, 1f, 295f, -1, 17)
				&& !StorageSnapshotSync.IsSnapshotStateEligible(true, true, 1f, 295f, 0, 0);
			return valid && rejected
				? UnitTestResult.Pass("Encoder, hash and apply share one supported-item boundary")
				: UnitTestResult.Fail("Storage snapshot accepted unsupported state or rejected valid state");
		}

		[UnitTest(name: "Storage snapshot removes only supported extras", category: "Networking")]
		public static UnitTestResult RemovalKeepsUnsupportedItems()
		{
			bool removesUnassigned = StorageSnapshotSync.ShouldRemoveSnapshotItem(
				stateEligible: true, netId: 0, desired: false,
				lastItemRevision: 0, snapshotRevision: 10);
			bool removesObsolete = StorageSnapshotSync.ShouldRemoveSnapshotItem(
				true, 41, false, 9, 10);
			bool keepsUnsupported = !StorageSnapshotSync.ShouldRemoveSnapshotItem(
				false, 0, false, 0, 10);
			bool keepsDesired = !StorageSnapshotSync.ShouldRemoveSnapshotItem(
				true, 42, true, 9, 10);
			bool keepsNewer = !StorageSnapshotSync.ShouldRemoveSnapshotItem(
				true, 43, false, 11, 10);
			return removesUnassigned && removesObsolete && keepsUnsupported
			       && keepsDesired && keepsNewer
				? UnitTestResult.Pass("Only supported unassigned or obsolete items are removed")
				: UnitTestResult.Fail("Snapshot removal crossed the supported-item boundary");
		}
	}
}
#endif
