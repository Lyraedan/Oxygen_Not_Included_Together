#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SoakKeyframePagingTests
	{
		[UnitTest(name: "Soak keyframe batches collapse small-entry ACKs", category: "Networking")]
		public static UnitTestResult SmallEntriesUseBoundedBatches()
		{
			const int entryCount = 3800;
			byte[] typicalEntry = new byte[256];
			int nextEntry = 0;
			int acknowledgements = 0;
			while (nextEntry < entryCount)
			{
				SoakHashDomainKeyframeBatchPacket batch =
					SoakHashDomainKeyframeBatchPacket.Create(17, 6, nextEntry);
				int firstEntry = nextEntry;
				while (nextEntry < entryCount && batch.TryAppend(typicalEntry))
					nextEntry++;
				if (nextEntry == firstEntry
				    || batch.GetOuterReliablePageCount() > 2
				    || batch.GetOrderedReliableWireBytes()
				    > SoakHashDomainKeyframeBatchPacket.MaxOrderedWireBytes)
					return UnitTestResult.Fail("A small-entry batch made no bounded progress");
				acknowledgements++;
			}
			return acknowledgements <= 128
				? UnitTestResult.Pass(
					$"{entryCount} small entries need {acknowledgements} cumulative ACKs")
				: UnitTestResult.Fail(
					$"Small entries still need {acknowledgements} ACKs");
		}

		[UnitTest(name: "Soak keyframe batch ACK releases exact credit", category: "Networking")]
		public static UnitTestResult BatchAckReleasesExactCredit()
		{
			SoakHashDomainKeyframeBatchPacket batch =
				SoakHashDomainKeyframeBatchPacket.Create(18, 7, 0);
			batch.TryAppend(new byte[128]);
			batch.TryAppend(new byte[128]);
			var window = new SoakKeyframeBatchProgressWindow
			{
				RunId = 18,
				SampleId = 7,
				ExpectedEntries = 4,
				ConnectionGeneration = 9,
				OutstandingBatch = batch,
			};
			var exact = new SoakKeyframeBatchAckPacket
			{
				RunId = 18, SampleId = 7, FirstEntryIndex = 0, ReceivedEntries = 2,
			};
			var gap = new SoakKeyframeBatchAckPacket
			{
				RunId = 18, SampleId = 7, FirstEntryIndex = 1, ReceivedEntries = 2,
			};
			var future = new SoakKeyframeBatchAckPacket
			{
				RunId = 18, SampleId = 7, FirstEntryIndex = 0, ReceivedEntries = 3,
			};
			return SoakKeyframeBatchAckPacket.Evaluate(window, exact, 9)
			       == SoakKeyframeBatchProgressResult.Advanced
			       && SoakKeyframeBatchAckPacket.Evaluate(window, gap, 9)
			       == SoakKeyframeBatchProgressResult.Invalid
			       && SoakKeyframeBatchAckPacket.Evaluate(window, future, 9)
			       == SoakKeyframeBatchProgressResult.Invalid
			       && SoakKeyframeBatchAckPacket.Evaluate(window, exact, 10)
			       == SoakKeyframeBatchProgressResult.Ignore
				? UnitTestResult.Pass("Only the exact generation-bound cumulative ACK releases credit")
				: UnitTestResult.Fail("A gap, future, or stale-generation ACK released credit");
		}

		[UnitTest(name: "Soak keyframe batches reject entry gaps", category: "Networking")]
		public static UnitTestResult BatchReceiverRejectsEntryGap()
		{
			SoakHashDomainKeyframeTracker.Begin(new SoakHashDomainKeyframeContext
			{
				RunId = 11,
				SampleId = 3,
				ExpectedEntries = 4,
				PagedTransport = true,
				LifecycleBaseline =
					new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>(),
			});
			SoakKeyframePageReceiver.Begin(11, 3, 4);
			SoakHashDomainKeyframeBatchPacket future =
				SoakHashDomainKeyframeBatchPacket.Create(11, 3, 1);
			future.TryAppend(SerializeKeyframeBody(128, 1));
			SoakKeyframeBatchReceiveResult result =
				SoakKeyframePageReceiver.Accept(future);
			bool noCredit = !SoakKeyframePageReceiver.TryGetPendingBatchAck(out _)
			                && SoakKeyframePageReceiver.CompletedEntries == 0
			                && !SoakHashDomainKeyframeTracker.HasFinished(11, 3);
			SoakHashDomainKeyframeTracker.Reset();
			return result == SoakKeyframeBatchReceiveResult.Invalid && noCredit
				? UnitTestResult.Pass("A future batch cannot skip the contiguous entry cursor")
				: UnitTestResult.Fail("A future batch released receiver credit");
		}

		[UnitTest(name: "Soak keyframe batch and page share one cursor", category: "Networking")]
		public static UnitTestResult BatchAndPageShareContiguousCursor()
		{
			SoakHashDomainKeyframeTracker.Begin(new SoakHashDomainKeyframeContext
			{
				RunId = 11,
				SampleId = 3,
				ExpectedEntries = 4,
				PagedTransport = true,
				LifecycleBaseline =
					new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>(),
			});
			SoakKeyframePageReceiver.Begin(11, 3, 4);
			SoakHashDomainKeyframeBatchPacket first =
				SoakHashDomainKeyframeBatchPacket.Create(11, 3, 0);
			first.TryAppend(SerializeKeyframeBody(128, 0));
			if (SoakKeyframePageReceiver.Accept(first)
			    != SoakKeyframeBatchReceiveResult.Advanced
			    || !SoakKeyframePageReceiver.TryGetPendingBatchAck(out var batchAck))
				return ResetAndFail("The first batch did not advance entry zero");
			SoakKeyframePageReceiver.CommitAck(batchAck);

			byte[] middleBody = SerializeKeyframeBody(128, 1);
			SoakHashDomainKeyframePagePacket middle =
				SoakHashDomainKeyframePagePacket.Create(11, 3, 1, 0, middleBody);
			if (SoakKeyframePageReceiver.Accept(middle)
			    != SoakKeyframePageReceiveResult.EntryComplete
			    || !SoakKeyframePageReceiver.TryGetPendingAck(out var pageAck))
				return ResetAndFail("The page did not continue after the batch");
			SoakKeyframePageReceiver.CommitAck(pageAck);

			SoakHashDomainKeyframeBatchPacket last =
				SoakHashDomainKeyframeBatchPacket.Create(11, 3, 2);
			last.TryAppend(SerializeKeyframeBody(128, 2));
			bool contiguous = SoakKeyframePageReceiver.Accept(last)
			                  == SoakKeyframeBatchReceiveResult.Advanced
			                  && SoakKeyframePageReceiver.CompletedEntries == 3
			                  && !SoakHashDomainKeyframeTracker.HasFinished(11, 3);
			SoakHashDomainKeyframeTracker.Reset();
			return contiguous
				? UnitTestResult.Pass("Batch and page transports advance one strict entry cursor")
				: UnitTestResult.Fail("Batch/page interop skipped or duplicated an entry");
		}

		[UnitTest(name: "Soak keyframe pages cap Riptide fragment bursts", category: "Networking")]
		public static UnitTestResult PageEnvelopeCapsRiptideFragments()
		{
			byte[] entry = new byte[12 * 1024 * 1024];
			int pageCount = SoakHashDomainKeyframePagePacket.PageCountFor(entry.Length);
			SoakHashDomainKeyframePagePacket page =
				SoakHashDomainKeyframePagePacket.Create(8, 2, 0, 0, entry);
			int fragments = (page.GetOrderedReliableWireBytes()
			                 + SoakHashDomainKeyframePagePacket.RiptideFragmentPayloadBytes - 1)
			                / SoakHashDomainKeyframePagePacket.RiptideFragmentPayloadBytes;
			return pageCount > 700
			       && page.Payload.Length <= SoakHashDomainKeyframePagePacket.MaxPayloadBytes
			       && page.GetOrderedReliableWireBytes()
			       <= SoakHashDomainKeyframePagePacket.MaxOrderedWireBytes
			       && fragments <= SoakHashDomainKeyframePagePacket.MaxRiptideFragmentsPerPage
			       && fragments < 16
				? UnitTestResult.Pass("A 12 MiB entry is emitted as bounded application pages")
				: UnitTestResult.Fail(
					$"Paging burst exceeded its cap: pages={pageCount}, fragments={fragments}");
		}

		[UnitTest(name: "Soak keyframe assembler rejects gaps without early apply", category: "Networking")]
		public static UnitTestResult AssemblerRejectsGapWithoutEarlyApply()
		{
			byte[] serialized = SerializeKeyframeBody(4 * 1024 * 1024);
			SoakHashDomainKeyframeTracker.Begin(11, 3, 2);
			SoakKeyframePageReceiver.Begin(11, 3, 2);
			SoakHashDomainKeyframePagePacket first =
				SoakHashDomainKeyframePagePacket.Create(11, 3, 0, 0, serialized);
			SoakHashDomainKeyframePagePacket second =
				SoakHashDomainKeyframePagePacket.Create(11, 3, 0, 1, serialized);
			SoakKeyframePageReceiveResult gap = SoakKeyframePageReceiver.Accept(second);
			bool noEarlyApply = !SoakHashDomainKeyframeTracker.HasFinished(11, 3)
			                    && SoakKeyframePageReceiver.CompletedEntries == 0;
			SoakKeyframePageReceiveResult accepted = SoakKeyframePageReceiver.Accept(first);
			bool bounded = SoakKeyframePageReceiver.BufferedBytes <= serialized.Length;
			SoakHashDomainKeyframeTracker.Reset();
			return gap == SoakKeyframePageReceiveResult.Invalid
			       && accepted == SoakKeyframePageReceiveResult.Advanced
			       && noEarlyApply && bounded && SoakKeyframePageReceiver.BufferedBytes == 0
				? UnitTestResult.Pass("Missing pages cannot release credit or apply a keyframe")
				: UnitTestResult.Fail("Gap, early apply, or assembler byte cap was not enforced");
		}

		[UnitTest(name: "Soak keyframe page ACK advances one exact page", category: "Networking")]
		public static UnitTestResult PageAckAdvancesOneExactPage()
		{
			byte[] entry = new byte[SoakHashDomainKeyframePagePacket.MaxPayloadBytes + 1];
			SoakHashDomainKeyframePagePacket page =
				SoakHashDomainKeyframePagePacket.Create(12, 4, 0, 0, entry);
			var window = new SoakKeyframePageProgressWindow
			{
				RunId = 12,
				SampleId = 4,
				ExpectedEntries = 2,
				OutstandingPage = page,
			};
			var exact = new SoakKeyframePageAckPacket
			{
				RunId = 12,
				SampleId = 4,
				EntryIndex = 0,
				AcknowledgedPages = 1,
				ReceivedEntries = 0,
			};
			var future = new SoakKeyframePageAckPacket
			{
				RunId = 12,
				SampleId = 4,
				EntryIndex = 0,
				AcknowledgedPages = 2,
				ReceivedEntries = 1,
			};
			return SoakKeyframePageAckPacket.Evaluate(window, exact)
			       == SoakKeyframePageProgressResult.Advanced
			       && SoakKeyframePageAckPacket.Evaluate(window, future)
			       == SoakKeyframePageProgressResult.Invalid
				? UnitTestResult.Pass("Only the exact cumulative page ACK releases one credit")
				: UnitTestResult.Fail("A missing or future page ACK released host credit");
		}

		[UnitTest(name: "Soak keyframe assembler preserves batch apply fence", category: "Networking")]
		public static UnitTestResult AssemblerPreservesBatchApplyFence()
		{
			byte[] serialized = SerializeKeyframeBody(4 * 1024 * 1024);
			SoakHashDomainKeyframeTracker.Begin(new SoakHashDomainKeyframeContext
			{
				RunId = 11,
				SampleId = 3,
				ExpectedEntries = 2,
				PagedTransport = true,
				LifecycleBaseline =
					new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>(),
			});
			SoakKeyframePageReceiver.Begin(11, 3, 2);
			int pageCount = SoakHashDomainKeyframePagePacket.PageCountFor(serialized.Length);
			for (int index = 0; index < pageCount; index++)
			{
				SoakKeyframePageReceiveResult result = SoakKeyframePageReceiver.Accept(
					SoakHashDomainKeyframePagePacket.Create(11, 3, 0, index, serialized));
				bool final = index == pageCount - 1;
				if (result != (final
					    ? SoakKeyframePageReceiveResult.EntryComplete
					    : SoakKeyframePageReceiveResult.Advanced)
				    || !SoakKeyframePageReceiver.TryGetPendingAck(out var ack))
				{
					SoakHashDomainKeyframeTracker.Reset();
					return UnitTestResult.Fail($"Page {index} did not produce exact progress");
				}
				SoakKeyframePageReceiver.CommitAck(ack);
			}
			bool fenced = SoakKeyframePageReceiver.CompletedEntries == 1
			              && SoakKeyframePageReceiver.BufferedBytes == 0
			              && !SoakHashDomainKeyframeTracker.HasFinished(11, 3);
			SoakHashDomainKeyframeTracker.Reset();
			return fenced
				? UnitTestResult.Pass("A complete entry is buffered but the two-entry batch is not applied")
				: UnitTestResult.Fail("A partial keyframe batch applied before every entry arrived");
		}

		private static UnitTestResult ResetAndFail(string message)
		{
			SoakHashDomainKeyframeTracker.Reset();
			return UnitTestResult.Fail(message);
		}

		private static byte[] SerializeKeyframeBody(int storageBytes, int entryIndex = 0)
		{
			var packet = new SoakHashDomainKeyframePacket
			{
				RunId = 11,
				SampleId = 3,
				EntryIndex = entryIndex,
				NetId = 21 + entryIndex,
				LifecycleSnapshot = new SpawnPrefabPacket
				{
					NetId = 21 + entryIndex,
					Revision = 1,
					Hash = 1,
					WorldId = 0,
					IsActive = true,
				},
				StorageRevision = 1,
				StorageSnapshots = new() { new byte[storageBytes] },
			};
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				packet.Serialize(writer);
			return stream.ToArray();
		}
	}
}
#endif
