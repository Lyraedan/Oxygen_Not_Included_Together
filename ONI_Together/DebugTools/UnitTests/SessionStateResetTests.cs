using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.DLC.Aquatic;
using ONI_Together.Networking.Packets.DLC;
using ONI_Together.Networking.Packets.DLC.Frosty;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DuplicantActions;
using ONI_Together.Patches.DLC.Aquatic;
using ONI_Together.Patches.Bionics;
using ONI_Together.Patches.DLC.Bionic;
using ONI_Together.Patches.DLC.Frosty;
using ONI_Together.Patches.DLC.SpacedOut;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SessionStateResetTests
	{
		[UnitTest(name: "Session reset clears core replay state", category: "Networking")]
		public static UnitTestResult ClearsCoreReplayState()
		{
			SessionStateReset.Reset();
			if (!HostBroadcastPacket.TryBeginRequest(42, 7, 3))
				return UnitTestResult.Fail("Fresh relay request was rejected before reset");
			if (HostBroadcastPacket.TryBeginRequest(42, 7, 3))
				return UnitTestResult.Fail("Duplicate relay request was accepted before reset");

			if (ChunkedPacket.GetNextSequenceId() != 0 || ChunkedPacket.GetNextSequenceId() != 1)
				return UnitTestResult.Fail("Chunk sequence did not advance before reset");

			SessionStateReset.Reset();
			SessionStateReset.Reset();
			if (!HostBroadcastPacket.TryBeginRequest(42, 7, 3))
				return UnitTestResult.Fail("Relay request history survived an idempotent session reset");
			if (ChunkedPacket.GetNextSequenceId() != 0)
				return UnitTestResult.Fail("Chunk sequence survived an idempotent session reset");

			return UnitTestResult.Pass("Core replay state starts fresh after every session reset");
		}

		[UnitTest(name: "Session reset clears Aquatic replay state", category: "Sync")]
		public static UnitTestResult ClearsAquaticReplayState()
		{
			SessionStateReset.Reset();
			if (OxyCoralSync.NextSequence(2, 100) != 1 || OxyCoralSync.NextSequence(2, 100) != 2)
				return UnitTestResult.Fail("OxyCoral host sequence did not advance");
			if (!OxyCoralBubblePacket.TryClaimSequence(2, 100, 20) ||
			    OxyCoralBubblePacket.TryClaimSequence(2, 100, 1))
				return UnitTestResult.Fail("OxyCoral client replay gate did not reject stale state");
			if (SeaTreeBranchSync.NextFruitSequence(7, true) != 1 ||
			    !SeaTreeBranchStatePacket.TryClaimFruitSequence(7, 20))
				return UnitTestResult.Fail("SeaTree fruit sequences did not advance");
			if (UnderwaterVentSync.NextBubbleSequence(3, 200, true) != 1 ||
			    !UnderwaterVentStatePacket.TryClaimBubble(3, 200, 20))
				return UnitTestResult.Fail("Underwater vent sequences did not advance");

			SessionStateReset.Reset();
			if (OxyCoralSync.NextSequence(2, 100) != 1 ||
			    !OxyCoralBubblePacket.TryClaimSequence(2, 100, 1) ||
			    SeaTreeBranchSync.NextFruitSequence(7, true) != 1 ||
			    !SeaTreeBranchStatePacket.TryClaimFruitSequence(7, 1) ||
			    UnderwaterVentSync.NextBubbleSequence(3, 200, true) != 1 ||
			    !UnderwaterVentStatePacket.TryClaimBubble(3, 200, 1))
				return UnitTestResult.Fail("Aquatic replay state survived session reset");

			return UnitTestResult.Pass("Aquatic replay state accepts new-world sequence one");
		}

		[UnitTest(name: "Session reset clears cross-DLC replay state", category: "Sync")]
		public static UnitTestResult ClearsCrossDlcReplayState()
		{
			SessionStateReset.Reset();
			if (!LargeImpactorOutcomePacket.TryClaimOutcome("event:1") ||
			    LargeImpactorOutcomePacket.TryClaimOutcome("event:1"))
				return UnitTestResult.Fail("Large Impactor outcome gate did not retain replay state");
			if (!ExplorerGeyserRevealSync.TryClaimOutcome("explorer:1") ||
			    ExplorerGeyserRevealSync.TryClaimOutcome("explorer:1"))
				return UnitTestResult.Fail("Explorer reveal gate did not retain replay state");
			if (RemoteWorkerDockSync.NextHostRevision(10) != 1 ||
			    RemoteWorkerDockSync.NextHostRevision(10) != 2 ||
			    RemoteWorkerDockSelectionSync.NextHostRevision(11) != 1 ||
			    BionicExplosionSync.NextSequence(12) != 1)
				return UnitTestResult.Fail("Bionic host revisions did not advance");
			if (CritterTrapGasSync.NextSequence(13) != 1 ||
			    !CritterTrapGasPacket.TryClaimSequence(13, 20) ||
			    SpaceTreeSeededCometSync.NextSequence(14) != 1 ||
			    !SpaceTreeImpactPacket.TryClaimSequence(14, 20))
				return UnitTestResult.Fail("Spaced Out or Frosty sequences did not advance");

			SessionStateReset.Reset();
			if (!LargeImpactorOutcomePacket.TryClaimOutcome("event:1") ||
			    !ExplorerGeyserRevealSync.TryClaimOutcome("explorer:1") ||
			    RemoteWorkerDockSync.NextHostRevision(10) != 1 ||
			    RemoteWorkerDockSelectionSync.NextHostRevision(11) != 1 ||
			    BionicExplosionSync.NextSequence(12) != 1 ||
			    CritterTrapGasSync.NextSequence(13) != 1 ||
			    !CritterTrapGasPacket.TryClaimSequence(13, 1) ||
			    SpaceTreeSeededCometSync.NextSequence(14) != 1 ||
			    !SpaceTreeImpactPacket.TryClaimSequence(14, 1))
				return UnitTestResult.Fail("A cross-DLC replay cache survived session reset");

			return UnitTestResult.Pass("Cross-DLC replay gates accept new-world sequence one");
		}

		[UnitTest(name: "Session reset clears Spaced Out discovery and apply guards", category: "Sync")]
		public static UnitTestResult ClearsSpacedOutDiscoveryState()
		{
			var state = new ClusterDiscoveryStatePacket
			{
				Kind = ClusterDiscoveryKind.Fog,
				LocationQ = 2,
				LocationR = -1,
				Progress = 0.5f
			};
			ClusterDiscoverySync.ResetSessionState();
			if (!ClusterDiscoverySync.TryRecordPercent(state) || ClusterDiscoverySync.TryRecordPercent(state))
				return UnitTestResult.Fail("Cluster discovery deduplication did not retain session state");
			SpacedOutSyncGuard.Begin();
			SessionStateReset.Reset();
			if (!ClusterDiscoverySync.TryRecordPercent(state) || SpacedOutSyncGuard.IsApplying)
				return UnitTestResult.Fail("Spaced Out discovery or RocketSettings apply guard survived reset");
			return UnitTestResult.Pass("Spaced Out discovery and shared RocketSettings guard start fresh");
		}

		[UnitTest(name: "Session reset clears subscriptions and sync flags", category: "Networking")]
		public static UnitTestResult ClearsSubscriptionsAndSyncFlags()
		{
			SessionStateReset.Reset();
			DuplicantChoreBroadcaster.SetSubscription(101, 7, true);
			StatusBroadcaster.SetSubscription(101, 8, true);
			GameServerHardSync.hardSyncDoneThisCycle = true;
			GameServerHardSync.IsHardSyncInProgress = true;
			SpacedOutSyncGuard.Begin();
			if (!SpacedOutSyncGuard.IsApplying)
				return UnitTestResult.Fail("Spaced Out guard did not enter applying state");

			SessionStateReset.Reset();
			if (DuplicantChoreBroadcaster.SubscribedNetIds.Count != 0 ||
			    DuplicantChoreBroadcaster.PendingImmediate.Count != 0 ||
			    StatusBroadcaster.SubscribedNetIds.Count != 0 ||
			    StatusBroadcaster.PendingImmediate.Count != 0)
				return UnitTestResult.Fail("Subscription state survived session reset");
			if (GameServerHardSync.hardSyncDoneThisCycle ||
			    GameServerHardSync.IsHardSyncInProgress || SpacedOutSyncGuard.IsApplying)
				return UnitTestResult.Fail("Sync lifecycle flags survived session reset");

			return UnitTestResult.Pass("Subscriptions and lifecycle flags start clean");
		}

		[UnitTest(name: "Session reset restores packet processing guards", category: "Networking")]
		public static UnitTestResult RestoresPacketProcessingGuards()
		{
			PacketHandler.readyToProcess = false;
			DuplicantPriorityPacket.IsApplying = true;

			SessionStateReset.Reset();
			if (!PacketHandler.readyToProcess || DuplicantPriorityPacket.IsApplying)
				return UnitTestResult.Fail("Packet processing or priority apply guard survived reset");

			return UnitTestResult.Pass("Packet processing and mutation guards start clean");
		}
	}
}
