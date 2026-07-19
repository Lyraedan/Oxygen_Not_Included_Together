#if DEBUG
using System.Collections.Generic;
using System.IO;
using ONI_Together.Misc.World;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.World;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SoakSegmentFenceTests
	{
		[UnitTest(name: "Soak failed keyframe requires authoritative hard sync", category: "Networking")]
		public static UnitTestResult FailedKeyframeRequiresHardSync()
		{
			return SoakStateHashProbe.RequiresAuthoritativeHardSync(keyframeApplied: false)
			       && !SoakStateHashProbe.RequiresAuthoritativeHardSync(keyframeApplied: true)
				? UnitTestResult.Pass("A partial keyframe cannot release the next simulation segment")
				: UnitTestResult.Fail("A partial keyframe could continue without full authoritative sync");
		}

		[UnitTest(name: "Soak application fence is exact and ordered", category: "Networking")]
		public static UnitTestResult ApplicationFenceIsExactAndOrdered()
		{
			var fence = RoundTrip(new SoakSegmentFencePacket
			{
				RunId = 7,
				SampleId = 4,
				CompletedTicks = 7_200,
				RepairSequenceCut = 73,
			});
			var ack = RoundTrip(new SoakSegmentFenceAckPacket
			{
				RunId = 7,
				SampleId = 4,
				CompletedTicks = 7_200,
				RepairSequenceCut = 73,
				KeyframeApplied = true,
			});
			if (fence.RunId != 7 || fence.SampleId != 4 || fence.CompletedTicks != 7_200
			    || fence.RepairSequenceCut != 73
			    || ack.RunId != 7 || ack.SampleId != 4 || ack.CompletedTicks != 7_200
			    || ack.RepairSequenceCut != 73
			    || !ack.KeyframeApplied
			    || fence is not IHostOnlyPacket || (object)ack is IHostOnlyPacket
			    || !OrderedReliableChannel.ShouldWrap(fence, PacketSendMode.ReliableImmediate))
			{
				return UnitTestResult.Fail("Soak fence lost its marker or bypassed the ordered reliable stream");
			}

			return UnitTestResult.Pass("Fence ACK identifies the exact ordered application boundary");
		}

		[UnitTest(name: "Soak fence waits for contiguous repair application", category: "Networking")]
		public static UnitTestResult FenceWaitsForContiguousRepairApplication()
		{
			WorldUpdatePacket.ResetRevisionState();
			try
			{
				WorldUpdatePacket.SetClientRepairBaseline(40);
				WorldUpdatePacket.ResolveRepairSequence(42);
				if (SoakStateHashProbe.CanAcknowledgeRepairFence(
					    WorldUpdatePacket.ClientResolvedRepairSequence, 42))
				{
					return UnitTestResult.Fail("Fence acknowledged across a missing repair sequence");
				}
				WorldUpdatePacket.ResolveRepairSequence(41);
				return WorldUpdatePacket.ClientResolvedRepairSequence == 42
				       && SoakStateHashProbe.CanAcknowledgeRepairFence(42, 42)
					? UnitTestResult.Pass("Fence waits for every out-of-order repair through its exact cut")
					: UnitTestResult.Fail("Contiguous repair resolution did not release the fence");
			}
			finally
			{
				WorldUpdatePacket.ResetRevisionState();
			}
		}

		[UnitTest(name: "World repair ACK is cumulative and bounded", category: "Networking")]
		public static UnitTestResult RepairAckIsCumulativeAndBounded()
		{
			var ack = RoundTrip(new WorldRepairAckPacket { AppliedThrough = 73 });
			var journal = new WorldUpdateRepairJournal(
				maxEntries: 1, maxUpdates: 2, replayIntervalSeconds: 1f);
			WorldUpdatePacket.ResetRevisionState();
			try
			{
				WorldUpdatePacket first = RepairPacket(1);
				WorldUpdatePacket second = RepairPacket(2);
				if (ack.AppliedThrough != 73
				    || !journal.TryRecordNext(first, new[] { 11UL, 12UL }, now: 0f)
				    || journal.TryRecordNext(second, new[] { 11UL }, now: 0f)
				    || second.RepairSequence != 0 || journal.PendingEntryCount != 1
				    || !journal.IsBackpressured)
					return UnitTestResult.Fail("Unacknowledged repair was evicted or consumed a sequence at capacity");
				if (!journal.AcceptAppliedAck(11, first.RepairSequence)
				    || journal.PendingEntryCount != 1
				    || journal.TryRecordNext(second, new[] { 11UL }, now: 0f)
				    || second.RepairSequence != 0
				    || !journal.AcceptAppliedAck(12, first.RepairSequence)
				    || journal.PendingEntryCount != 0
				    || !journal.TryRecordNext(second, new[] { 11UL }, now: 0f)
				    || second.RepairSequence != first.RepairSequence + 1)
					return UnitTestResult.Fail("Per-client cumulative ACK released another client's obligation");
				return UnitTestResult.Pass("Journal waits for every client's cumulative ACK without a sequence gap");
			}
			finally
			{
				journal.Reset();
				WorldUpdatePacket.ResetRevisionState();
			}
		}

		[UnitTest(name: "World repair journal replays ordered before a fence", category: "Networking")]
		public static UnitTestResult RepairJournalReplaysOrderedBeforeFence()
		{
			var journal = new WorldUpdateRepairJournal(4, 8, 1f);
			var replayed = new List<long>();
			var paced = new List<long>();
			WorldUpdatePacket.ResetRevisionState();
			try
			{
				WorldUpdatePacket first = RepairPacket(1);
				WorldUpdatePacket second = RepairPacket(2);
				if (!journal.TryRecordNext(first, new[] { 11UL }, now: 0f)
				    || !journal.TryRecordNext(second, new[] { 11UL }, now: 0f)
				    || !journal.ReplayPendingThrough(second.RepairSequence,
					    (client, packet) =>
					    {
						    replayed.Add(packet.RepairSequence);
						    return client == 11;
					    })
				    || replayed.Count != 2 || replayed[0] != 1 || replayed[1] != 2
				    || journal.RetransmitCount != 2)
					return UnitTestResult.Fail("Fence replay skipped, reordered, or failed to count repair retransmits");
				if (journal.ReplayOneDue(0.5f, (_, _) => true)
				    || !journal.ReplayOneDue(1.1f, (_, packet) =>
				    {
					    paced.Add(packet.RepairSequence);
					    return true;
				    })
				    || !journal.ReplayOneDue(2.2f, (_, packet) =>
				    {
					    paced.Add(packet.RepairSequence);
					    return true;
				    })
				    || paced.Count != 2 || paced[0] != 1 || paced[1] != 2)
					return UnitTestResult.Fail("Periodic replay was unpaced or starved a later repair");
				return UnitTestResult.Pass("Pending repairs replay in sequence with paced round-robin retry");
			}
			finally
			{
				journal.Reset();
				WorldUpdatePacket.ResetRevisionState();
			}
		}

		[UnitTest(name: "World repair journal respects client observation window", category: "Networking")]
		public static UnitTestResult RepairJournalRespectsClientObservationWindow()
		{
			const ulong clientId = 11;
			int observationWindow = WorldUpdateRepairObservability.MaxPendingPackets;
			var journal = new WorldUpdateRepairJournal();
			var recorded = new List<WorldUpdatePacket>();
			WorldUpdatePacket.ResetRevisionState();
			try
			{
				for (int cell = 0; cell < observationWindow; cell++)
				{
					WorldUpdatePacket packet = RepairPacket(cell);
					if (!journal.TryRecordNext(packet, new[] { clientId }, now: 0f))
						return UnitTestResult.Fail("Host window closed before the client observation limit");
					recorded.Add(packet);
				}

				WorldUpdatePacket overflow = RepairPacket(observationWindow);
				if (journal.TryRecordNext(overflow, new[] { clientId }, now: 0f)
				    || overflow.RepairSequence != 0
				    || journal.PendingEntryCount != observationWindow)
				{
					return UnitTestResult.Fail(
						"Host exceeded the client repair observation window before ACK");
				}

				if (!journal.AcceptAppliedAck(clientId, recorded[0].RepairSequence)
				    || !journal.TryRecordNext(overflow, new[] { clientId }, now: 0f)
				    || overflow.RepairSequence != recorded[^1].RepairSequence + 1)
				{
					return UnitTestResult.Fail("Cumulative ACK did not reopen exactly one window slot");
				}

				return UnitTestResult.Pass(
					"Host backpressure bounds unobserved repairs to the client window");
			}
			finally
			{
				journal.Reset();
				WorldUpdatePacket.ResetRevisionState();
			}
		}

		[UnitTest(name: "World repair journal respects client observation update limit", category: "Networking")]
		public static UnitTestResult RepairJournalRespectsClientObservationUpdateLimit()
		{
			const ulong clientId = 11;
			int updateLimit = WorldUpdateRepairObservability.MaxPendingUpdates;
			var journal = new WorldUpdateRepairJournal();
			WorldUpdatePacket.ResetRevisionState();
			try
			{
				WorldUpdatePacket full = RepairPacket(0);
				for (int cell = 1; cell < updateLimit; cell++)
					full.Updates.Add(new WorldUpdatePacket.CellUpdate { Cell = cell });
				if (!journal.TryRecordNext(full, new[] { clientId }, now: 0f))
					return UnitTestResult.Fail("Host update window closed before the client limit");

				WorldUpdatePacket overflow = RepairPacket(updateLimit);
				if (journal.TryRecordNext(overflow, new[] { clientId }, now: 0f)
				    || overflow.RepairSequence != 0)
				{
					return UnitTestResult.Fail(
						"Host exceeded the client repair update limit before ACK");
				}

				if (!journal.AcceptAppliedAck(clientId, full.RepairSequence)
				    || !journal.TryRecordNext(overflow, new[] { clientId }, now: 0f))
					return UnitTestResult.Fail("ACK did not reopen the repair update window");

				return UnitTestResult.Pass(
					"Host update backpressure matches the client observation limit");
			}
			finally
			{
				journal.Reset();
				WorldUpdatePacket.ResetRevisionState();
			}
		}

		[UnitTest(name: "World repair journal releases disconnected clients explicitly", category: "Networking")]
		public static UnitTestResult RepairJournalDropsDisconnectedClient()
		{
			var journal = new WorldUpdateRepairJournal(1, 2, 1f);
			WorldUpdatePacket.ResetRevisionState();
			try
			{
				WorldUpdatePacket first = RepairPacket(1);
				WorldUpdatePacket second = RepairPacket(2);
				if (!journal.TryRecordNext(first, new[] { 11UL, 12UL }, 0f)
				    || journal.TryRecordNext(second, new[] { 12UL }, 0f)
				    || journal.DropClient(11) != 0 || journal.PendingEntryCount != 1
				    || journal.DropClient(12) != 1 || journal.PendingEntryCount != 0
				    || !journal.TryRecordNext(second, new[] { 12UL }, 0f))
					return UnitTestResult.Fail("Disconnect pruning dropped shared state or failed to release capacity");
				return UnitTestResult.Pass("Disconnect lifecycle explicitly prunes only the departed client's obligations");
			}
			finally
			{
				journal.Reset();
				WorldUpdatePacket.ResetRevisionState();
			}
		}

		[UnitTest(name: "World repair replay cannot starve fresh dispatch", category: "Networking")]
		public static UnitTestResult RepairReplayPreservesFreshDispatchBudget()
		{
			return WorldUpdateBatcher.HasFreshRepairBudget(
				       foregroundDispatched: false, replayed: true)
			       && WorldUpdateBatcher.HasFreshRepairBudget(
				       foregroundDispatched: false, replayed: false)
			       && !WorldUpdateBatcher.HasFreshRepairBudget(
				       foregroundDispatched: true, replayed: false)
				? UnitTestResult.Pass("Each non-foreground frame retains one fresh repair slot after replay")
				: UnitTestResult.Fail("Repair replay consumed or foreground bypassed the fresh dispatch budget");
		}

		[UnitTest(name: "World repair resolves only after cross-frame observation", category: "Networking")]
		public static UnitTestResult RepairRequiresGridObservationOrHigherRevision()
		{
			WorldUpdatePacket.ResetRevisionState();
			try
			{
				WorldUpdatePacket packet = RepairPacket(7);
				packet.Revision = 10;
				packet.RepairSequence = 1;
				if (!WorldUpdateRepairObservability.Track(packet, packet.Updates)
				    || WorldUpdatePacket.ClientResolvedRepairSequence != 0)
					return UnitTestResult.Fail("Repair resolved in the ModifyCell dispatch frame");
				WorldUpdateRepairObservability.ObserveForTests(_ => false, _ => 10);
				if (WorldUpdatePacket.ClientResolvedRepairSequence != 0)
					return UnitTestResult.Fail("Unobservable repair crossed the application fence");
				WorldUpdateRepairObservability.ObserveForTests(_ => false, _ => 11);
				if (WorldUpdatePacket.ClientResolvedRepairSequence != 1)
					return UnitTestResult.Fail("Strictly newer cell revision did not supersede the repair");
				if (!WorldUpdateRepairObservability.Track(packet, packet.Updates)
				    || WorldUpdateRepairObservability.PendingCount != 0)
					return UnitTestResult.Fail("Replay recreated an already resolved observation");

				WorldUpdatePacket.SetClientRepairBaseline(1);
				packet = RepairPacket(8);
				packet.Revision = 12;
				packet.RepairSequence = 2;
				WorldUpdateRepairObservability.Track(packet, packet.Updates);
				WorldUpdateRepairObservability.ObserveForTests(_ => true, _ => 12);
				return WorldUpdatePacket.ClientResolvedRepairSequence == 2
					? UnitTestResult.Pass("Repair resolves only after Grid visibility or strict supersession")
					: UnitTestResult.Fail("Grid-observed repair did not advance the cumulative cut");
			}
			finally
			{
				WorldUpdatePacket.ResetRevisionState();
			}
		}

		[UnitTest(name: "World dispatch freeze is versioned and blocks all classes", category: "Networking")]
		public static UnitTestResult WorldDispatchFreezeIsVersioned()
		{
			WorldUpdateBatcher.ResetSessionState();
			bool leaseHeld = false;
			try
			{
				leaseHeld = WorldUpdateBatcher.TryBeginWorldDispatchForTests();
				if (!leaseHeld || WorldUpdateBatcher.TryFreezeWorldDispatch(out _, out _))
					return UnitTestResult.Fail("Freeze crossed an in-flight world send");
				WorldUpdateBatcher.CompleteWorldDispatchForTests();
				leaseHeld = false;
				if (!WorldUpdateBatcher.TryFreezeWorldDispatch(
				    out long repairCut, out long mutationVersion)
				    || repairCut != 0
				    || !WorldUpdateBatcher.IsFrozenCheckpointValid(mutationVersion))
					return UnitTestResult.Fail("Empty world dispatch could not freeze exactly");
				bool backgroundQueued = WorldUpdateBatcher.QueueForTests(
					new WorldUpdatePacket.CellUpdate
					{
						Cell = 8,
						ReplaceType = SimMessages.ReplaceType.Replace,
					}, backgroundRepair: true);
				if (backgroundQueued
				    || !WorldUpdateBatcher.IsFrozenCheckpointValid(mutationVersion))
					return UnitTestResult.Fail(
						"Periodic background repair invalidated a stable checkpoint");
				WorldUpdateBatcher.QueueForTests(new WorldUpdatePacket.CellUpdate
				{
					Cell = 9,
					ReplaceType = SimMessages.ReplaceType.Replace,
				});
				WorldUpdateBatcher.Flush();
				if (WorldUpdateBatcher.IsFrozenCheckpointValid(mutationVersion)
				    || WorldUpdateBatcher.TryTakePendingDispatch(
					    out _, out _, requireReadyClients: false))
					return UnitTestResult.Fail("Foreground mutation crossed the frozen checkpoint");
				WorldUpdateBatcher.ResumeRepairDispatch();
				return WorldUpdateBatcher.TryTakePendingDispatch(
					       out WorldUpdatePacket packet, out _, requireReadyClients: false)
				       && !packet.IsBackgroundRepair
					? UnitTestResult.Pass("Freeze blocks every world dispatch and exposes mutation invalidation")
					: UnitTestResult.Fail("Queued foreground did not resume after the checkpoint");
			}
			finally
			{
				if (leaseHeld)
					WorldUpdateBatcher.CompleteWorldDispatchForTests();
				WorldUpdateBatcher.ResetSessionState();
			}
		}

		[UnitTest(name: "World update keeps valid host numeric semantics exact", category: "Networking")]
		public static UnitTestResult KeepsHostNumericSemanticsExact()
		{
			var additive = new WorldUpdatePacket.CellUpdate
			{
				Mass = -0.5f, Temperature = 280f, ReplaceType = SimMessages.ReplaceType.None
			};
			if (!WorldUpdatePacket.TryGetApplyValues(additive, out float temperature, out float mass)
			    || temperature != 280f || mass != -0.5f)
				return UnitTestResult.Fail("A valid additive removal was changed or rejected");
			var vacuum = new WorldUpdatePacket.CellUpdate
			{
				Mass = 0f, Temperature = 123f, ReplaceType = SimMessages.ReplaceType.Replace
			};
			if (!WorldUpdatePacket.TryGetApplyValues(vacuum, out temperature, out mass)
			    || temperature != 123f || mass != 0f)
				return UnitTestResult.Fail("An exact vacuum temperature was rewritten");
			vacuum.Mass = -1f;
			return !WorldUpdatePacket.TryGetApplyValues(vacuum, out _, out _)
				? UnitTestResult.Pass("Finite host operation values remain exact and corrupt absolutes are rejected")
				: UnitTestResult.Fail("A negative absolute mass was accepted");
		}

		[UnitTest(name: "World repair staging reports capacity backpressure", category: "Networking")]
		public static UnitTestResult RepairStagingReportsCapacityBackpressure()
		{
			WorldUpdateBatcher.ResetSessionState();
			try
			{
				for (int cell = 0; cell < 65536; cell++)
				{
					if (!WorldUpdateBatcher.QueueForTests(new WorldUpdatePacket.CellUpdate
					    {
						    Cell = cell,
						    ReplaceType = SimMessages.ReplaceType.Replace,
					    }, backgroundRepair: true))
						return UnitTestResult.Fail("Repair staging rejected an entry below capacity");
				}
				bool rejected = !WorldUpdateBatcher.QueueForTests(
					new WorldUpdatePacket.CellUpdate
					{
						Cell = 65536,
						ReplaceType = SimMessages.ReplaceType.Replace,
					}, backgroundRepair: true);
				bool overwriteAccepted = WorldUpdateBatcher.QueueForTests(
					new WorldUpdatePacket.CellUpdate
					{
						Cell = 0,
						ElementIdx = 7,
						ReplaceType = SimMessages.ReplaceType.Replace,
					}, backgroundRepair: true);
				if (!rejected || !overwriteAccepted
				    || WorldUpdateBatcher.PendingCountForTests(true) != 65536
				    || WorldUpdateBatcher.Flush() <= 0)
					return UnitTestResult.Fail("Repair staging hid capacity loss or rejected a safe overwrite");
				int retained = WorldUpdateBatcher.PendingCountForTests(true);
				int dispatched = 0;
				while (WorldUpdateBatcher.TryTakePendingDispatch(
					       out _, out _, requireReadyClients: false))
					dispatched++;
				if (retained <= 0 || retained >= 65536 || dispatched != 256
				    || WorldUpdateBatcher.Flush() <= 0
				    || WorldUpdateBatcher.PendingCountForTests(true) >= retained)
					return UnitTestResult.Fail("Bounded dispatch capacity deadlocked a larger staged repair set");
				return UnitTestResult.Pass("Repair staging drains bounded prefixes without loss or eviction");
			}
			finally
			{
				WorldUpdateBatcher.ResetSessionState();
			}
		}

		[UnitTest(name: "Soak raw and observed hashes reject the first mismatch", category: "Networking")]
		public static UnitTestResult RawAndObservedHashesRequireExactEquality()
		{
			var raw = new SoakHashCheckpointPacket
			{
				RunId = 3,
				SampleId = 2,
				CompletedTicks = 3_600,
				Cycle = 4,
				CycleTime = 12f,
			};
			var observed = new SoakHashReportPacket
			{
				RunId = 3,
				SampleId = 2,
				CompletedTicks = 3_600,
				Cycle = 4,
				CycleTime = 12f,
			};
			if (!SoakStateHashProbe.RawAndObservedHashesMatch(raw, observed))
				return UnitTestResult.Fail("Equal raw and observed hashes were rejected");
			observed.Lifecycle.UnassignedLiveCount = 1;
			if (SoakStateHashProbe.RawAndObservedHashesMatch(raw, observed))
				return UnitTestResult.Fail("Raw comparison ignored unassigned lifecycle state");
			observed.Lifecycle.UnassignedLiveCount = 0;
			observed.CycleTime = 12.001f;
			if (!SoakStateHashProbe.RawAndObservedHashesMatch(raw, observed))
				return UnitTestResult.Fail("Raw comparison rejected sub-frame client clock skew");
			observed.CycleTime = 13f;
			if (SoakStateHashProbe.RawAndObservedHashesMatch(raw, observed))
				return UnitTestResult.Fail("Raw comparison ignored client clock drift");
			raw.CycleTime = 599.999f;
			observed.Cycle = 5;
			observed.CycleTime = 0.001f;
			if (!SoakStateHashProbe.RawAndObservedHashesMatch(raw, observed))
				return UnitTestResult.Fail("Raw comparison rejected a cycle-boundary clock skew");
			raw.CycleTime = 12f;
			observed.Cycle = 4;
			observed.CycleTime = 12f;
			observed.StorageMembershipHash[0] = 1;
			return !SoakStateHashProbe.RawAndObservedHashesMatch(raw, observed)
				? UnitTestResult.Pass("The first differing domain rejects the sample")
				: UnitTestResult.Fail("A raw/observed hash mismatch was accepted");
		}

		[UnitTest(name: "Soak segment markers reject stale and duplicate acknowledgements", category: "Networking")]
		public static UnitTestResult SegmentMarkersAreGenerationBound()
		{
			if (!SoakTickBarrier.IsNextSegment((0, 0), 7, 1)
			    || !SoakTickBarrier.IsNextSegment((7, 1), 7, 2)
			    || SoakTickBarrier.IsNextSegment((7, 1), 7, 1)
			    || SoakTickBarrier.IsNextSegment((7, 1), 6, 2)
			    || SoakTickBarrier.IsNextSegment((0, 0), 7, 2))
			{
				return UnitTestResult.Fail("Soak segment sequence accepted a stale or skipped marker");
			}

			var ready = RoundTrip(new SoakTickReadyAckPacket
			{
				RunId = 7,
				SampleId = 4,
				Ready = true,
			});
			var start = RoundTrip(new SoakTickStartPacket { RunId = 7, SampleId = 4 });
			if (ready.RunId != 7 || ready.SampleId != 4 || !ready.Ready
			    || start.RunId != 7 || start.SampleId != 4)
			{
				return UnitTestResult.Fail("Soak ready/start packets lost the segment marker");
			}

			return UnitTestResult.Pass("Soak segment ACKs are bound to one run and sample");
		}

		[UnitTest(name: "New soak run supersedes stale client state", category: "Networking")]
		public static UnitTestResult NewRunSupersedesDroppedCancelState()
		{
			if (!SoakStateHashProbe.ShouldSupersedeClientRun(7, 8, 1)
			    || SoakStateHashProbe.ShouldSupersedeClientRun(7, 8, 2)
			    || SoakStateHashProbe.ShouldSupersedeClientRun(7, 7, 1)
			    || SoakStateHashProbe.ShouldSupersedeClientRun(0, 8, 1))
				return UnitTestResult.Fail("Dropped cancel can poison the next run");
			return UnitTestResult.Pass(
				"Host sample one explicitly supersedes stale client run state");
		}

		[UnitTest(name: "Soak causal fences cover 37800 fixed simulation ticks", category: "Networking")]
		public static UnitTestResult SegmentScheduleAndCheckpointAreExact()
		{
			int segmentTicks = SoakStateHashProbe.SegmentTickCount;
			int segmentCount = SoakStateHashProbe.SegmentCount;
			int targetTicks = SoakStateHashProbe.TargetTickCount;
			if (segmentTicks != 1_800 || segmentCount != 21 || targetTicks != 37_800
			    || targetTicks < 600 * 60)
			{
				return UnitTestResult.Fail("Soak segment schedule does not cover the required game cycle");
			}

			var checkpoint = RoundTrip(new SoakHashCheckpointPacket
			{
				RunId = 7,
				SampleId = 4,
				CompletedTicks = 7_200,
				Cycle = 5,
				CycleTime = 12f,
			});
			return checkpoint.SampleId == 4 && checkpoint.CompletedTicks == 7_200
				? UnitTestResult.Pass("Every hash checkpoint carries its completed causal segment")
				: UnitTestResult.Fail("Soak checkpoint lost its causal tick marker");
		}

		private static T RoundTrip<T>(T source) where T : IPacket, new()
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
				source.Serialize(writer);
			stream.Position = 0;
			var copy = new T();
			using var reader = new BinaryReader(stream);
			copy.Deserialize(reader);
			return copy;
		}

		private static WorldUpdatePacket RepairPacket(int cell)
		{
			var packet = new WorldUpdatePacket { Revision = cell + 1 };
			packet.Updates.Add(new WorldUpdatePacket.CellUpdate
			{
				Cell = cell,
				ElementIdx = 1,
				Mass = 1f,
				Temperature = 290f,
				ReplaceType = SimMessages.ReplaceType.Replace,
			});
			return packet;
		}
	}
}
#endif
