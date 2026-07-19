#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SoakGridReconcileTests
	{
		[UnitTest(name: "Soak grid reconcile plans bounded complete chunks", category: "Networking")]
		public static UnitTestResult ChunkPlanIsBoundedAndComplete()
		{
			List<SoakGridCell> cells = Cells(5);
			IReadOnlyList<SoakGridReconcileChunkPacket> chunks = SoakGridChunkPlanner.Plan(
				new SoakGridMarker(7, 4, 11, 101), cells, 2);
			if (chunks.Count != 3 || chunks[0].Cells.Count != 2
			    || chunks[1].Cells.Count != 2 || chunks[2].Cells.Count != 1)
				return UnitTestResult.Fail("Chunk planner did not respect the two-cell bound");
			if (chunks.SelectMany(chunk => chunk.Cells).Select(cell => cell.Cell)
			    .SequenceEqual(cells.Select(cell => cell.Cell)) == false)
				return UnitTestResult.Fail("Chunk planner lost or reordered grid cells");
			for (int i = 0; i < chunks.Count; i++)
			{
				SoakGridReconcileChunkPacket chunk = RoundTrip(chunks[i]);
				if (chunk.RunId != 7 || chunk.SampleId != 4 || chunk.Generation != 11
				    || chunk.WorldUpdateCut != 101
				    || chunk.ChunkIndex != i || chunk.TotalChunks != 3 || chunk.TotalRecords != 5
				    || chunk.Cells.Count > 2
				    || !chunk.ChunkHash.SequenceEqual(SoakGridProof.HashCells(chunk.Cells)))
					return UnitTestResult.Fail("Chunk wire payload lost its exact marker, bounds, or proof");
			}
			return UnitTestResult.Pass("Full-grid chunks are bounded, complete, and proof-bound");
		}

		[UnitTest(name: "Soak grid reconcile waits for complete delayed exact apply", category: "Networking")]
		public static UnitTestResult DriftReconcileWaitsForExactGrid()
		{
			List<SoakGridCell> expected = Cells(3);
			IReadOnlyList<SoakGridReconcileChunkPacket> chunks = SoakGridChunkPlanner.Plan(
				new SoakGridMarker(9, 2, 17, 102), expected, 2);
			var session = new SoakGridReconcileSession(maxApplyAttempts: 3);
			var actual = expected.ToDictionary(cell => cell.Cell, cell => cell);
			actual[1] = WithMass(actual[1], 999f);
			var delayed = new Dictionary<int, SoakGridCell>();
			int applyCount = 0;

			if (session.Accept(chunks[0]) != SoakGridAcceptResult.Accepted
			    || session.Pump(
				    cell => actual.TryGetValue(cell.Cell, out SoakGridCell value) && value.Equals(cell),
				    cell => { applyCount++; delayed[cell.Cell] = cell; },
				    () => SoakGridProof.FromCells(actual.Values)) != SoakGridPumpResult.WaitingForChunks
			    || applyCount != 0)
				return UnitTestResult.Fail("Partial chunk mutated the grid or advanced the barrier");

			if (session.Accept(chunks[1]) != SoakGridAcceptResult.Accepted
			    || session.Accept(chunks[1]) != SoakGridAcceptResult.Duplicate)
				return UnitTestResult.Fail("Duplicate chunk was not idempotent");
			if (session.Pump(
				    cell => actual.TryGetValue(cell.Cell, out SoakGridCell value) && value.Equals(cell),
				    cell => { applyCount++; delayed[cell.Cell] = cell; },
				    () => SoakGridProof.FromCells(actual.Values)) != SoakGridPumpResult.WaitingForApply
			    || applyCount != 1 || session.TryBuildAck(out _))
				return UnitTestResult.Fail("Client acknowledged before delayed simulation apply became observable");

			foreach (KeyValuePair<int, SoakGridCell> update in delayed)
				actual[update.Key] = update.Value;
			if (session.Pump(
				    cell => actual.TryGetValue(cell.Cell, out SoakGridCell value) && value.Equals(cell),
				    _ => applyCount++,
				    () => SoakGridProof.FromCells(actual.Values)) != SoakGridPumpResult.Complete
			    || !session.TryBuildAck(out SoakGridReconcileAckPacket ack)
			    || ack.ChunkIndex != chunks.Count - 1 || applyCount != 1)
				return UnitTestResult.Fail("Exact delayed grid state did not complete with one final proof ACK");
			return UnitTestResult.Pass("Client emits one proof ACK only after the exact grid is observable");
		}

		[UnitTest(name: "Soak grid reconcile retries are bounded without fake ACK", category: "Networking")]
		public static UnitTestResult ApplyRetryExhaustionAbortsWithoutAck()
		{
			SoakGridReconcileChunkPacket chunk = SoakGridChunkPlanner.Plan(
				new SoakGridMarker(3, 1, 5, 103), Cells(1), 4).Single();
			var session = new SoakGridReconcileSession(maxApplyAttempts: 2);
			session.Accept(chunk);
			Func<SoakGridProof> wrongProof = () => SoakGridProof.FromCells(
				new[] { WithMass(chunk.Cells[0], 777f) });
			if (session.Pump(_ => false, _ => { }, wrongProof) != SoakGridPumpResult.WaitingForApply
			    || session.Pump(_ => false, _ => { }, wrongProof) != SoakGridPumpResult.WaitingForApply
			    || session.Pump(_ => false, _ => { }, wrongProof) != SoakGridPumpResult.WaitingForApply
			    || session.Pump(_ => false, _ => { }, wrongProof) != SoakGridPumpResult.Aborted
			    || session.TryBuildAck(out _))
				return UnitTestResult.Fail("Retry exhaustion completed or emitted a false acknowledgement");
			return UnitTestResult.Pass("Apply retry exhaustion aborts without any ACK proof");
		}

		[UnitTest(name: "Soak grid reconcile ACKs reject stale and wrong proofs", category: "Networking")]
		public static UnitTestResult AckTrackerRequiresExactMarkerAndProof()
		{
			IReadOnlyList<SoakGridReconcileChunkPacket> chunks = SoakGridChunkPlanner.Plan(
				new SoakGridMarker(8, 6, 23, 104), Cells(3), 2);
			var tracker = new SoakGridAckTracker(new ulong[] { 42, 43 }, chunks);
			SoakGridReconcileAckPacket ack = RoundTrip(
				SoakGridReconcileSession.BuildExactAck(chunks.Last()));
			SoakGridReconcileAckPacket early = RoundTrip(
				SoakGridReconcileSession.BuildExactAck(chunks.First()));
			SoakGridReconcileAckPacket stale = CloneAck(ack);
			stale.Generation--;
			SoakGridReconcileAckPacket wrongCut = CloneAck(ack);
			wrongCut.WorldUpdateCut--;
			SoakGridReconcileAckPacket wrong = CloneAck(ack);
			wrong.ChunkHash[0] ^= 0xff;
			if (tracker.Accept(42, stale) != SoakGridAckResult.Rejected
			    || tracker.Accept(42, wrongCut) != SoakGridAckResult.Rejected
			    || tracker.Accept(42, wrong) != SoakGridAckResult.Rejected
			    || tracker.Accept(42, early) != SoakGridAckResult.Rejected
			    || tracker.Accept(99, ack) != SoakGridAckResult.Rejected
			    || tracker.Accept(42, ack) != SoakGridAckResult.Accepted
			    || tracker.Accept(42, ack) != SoakGridAckResult.Duplicate
			    || tracker.IsComplete
			    || tracker.Accept(43, ack) != SoakGridAckResult.Complete
			    || !tracker.IsComplete)
				return UnitTestResult.Fail("Host accepted stale/wrong proof or counted a duplicate ACK twice");
			return UnitTestResult.Pass("Host advances once every client submits the exact final grid proof");
		}

		[UnitTest(name: "Soak grid reconcile send cursor is ordered and bounded", category: "Networking")]
		public static UnitTestResult ChunkSendCursorIsOrderedAndBounded()
		{
			IReadOnlyList<SoakGridReconcileChunkPacket> chunks = SoakGridChunkPlanner.Plan(
				new SoakGridMarker(5, 3, 19, 105), Cells(5), 2);
			var cursor = new SoakGridChunkSendCursor(chunks);
			var sent = new List<int>();
			while (cursor.TryTakeNext(out SoakGridReconcileChunkPacket chunk))
				sent.Add(chunk.ChunkIndex);
			if (!sent.SequenceEqual(new[] { 0, 1, 2 }) || !cursor.IsComplete
			    || cursor.TryTakeNext(out _))
				return UnitTestResult.Fail("Chunk send cursor skipped, reordered, or replayed payload");
			return UnitTestResult.Pass("Grid chunks are exposed exactly once in wire order");
		}

		[UnitTest(name: "Soak grid reconcile client work is frame-budgeted", category: "Networking")]
		public static UnitTestResult ClientWorkIsFrameBudgeted()
		{
			List<SoakGridCell> cells = Cells(SoakGridReconcileSession.ScanBudget + 5);
			IReadOnlyList<SoakGridReconcileChunkPacket> chunks = SoakGridChunkPlanner.Plan(
				new SoakGridMarker(6, 2, 21, 106), cells);
			var session = new SoakGridReconcileSession(maxApplyAttempts: 300);
			foreach (SoakGridReconcileChunkPacket chunk in chunks)
				session.Accept(chunk);
			int matchCalls = 0;
			int applyCalls = 0;
			SoakGridPumpResult first = session.Pump(
				_ => { matchCalls++; return false; }, _ => applyCalls++,
				() => SoakGridProof.FromCells(cells));
			if (first != SoakGridPumpResult.WaitingForApply
			    || matchCalls != SoakGridReconcileSession.ScanBudget || applyCalls != 0)
				return UnitTestResult.Fail("Initial exact-grid scan exceeded its frame budget");
			SoakGridPumpResult second = session.Pump(
				_ => { matchCalls++; return false; }, _ => applyCalls++,
				() => SoakGridProof.FromCells(cells));
			if (second != SoakGridPumpResult.WaitingForApply
			    || matchCalls != cells.Count
			    || applyCalls != SoakGridReconcileSession.ApplyBudget)
				return UnitTestResult.Fail("Grid apply did not start with its fixed frame budget");
			return UnitTestResult.Pass("Full-grid scan and apply are bounded across frames");
		}

		[UnitTest(name: "Soak grid reconcile wire bounds reject oversized chunks", category: "Networking")]
		public static UnitTestResult WireBoundsRejectOversizedChunk()
		{
			var packet = new SoakGridReconcileChunkPacket
			{
				RunId = 1,
				SampleId = 1,
				Generation = 1,
				WorldUpdateCut = 107,
				ChunkIndex = 0,
				TotalChunks = 1,
				TotalRecords = SoakGridWire.MaxCellsPerChunk + 1,
				Cells = Cells(SoakGridWire.MaxCellsPerChunk + 1),
			};
			packet.FullGridHash = SoakGridProof.HashCells(packet.Cells);
			packet.ChunkHash = packet.FullGridHash.ToArray();
			try
			{
				RoundTrip(packet);
				return UnitTestResult.Fail("Oversized grid reconcile chunk was accepted");
			}
			catch (InvalidDataException)
			{
				return UnitTestResult.Pass("Grid reconcile chunk wire size is bounded");
			}
		}

		private static List<SoakGridCell> Cells(int count)
		{
			var cells = new List<SoakGridCell>(count);
			for (int cell = 0; cell < count; cell++)
			{
				cells.Add(new SoakGridCell
				{
					Cell = cell,
					ElementIdx = (ushort)(cell + 1),
					Temperature = 280f + cell,
					Mass = 10f + cell,
					DiseaseIdx = (byte)cell,
					DiseaseCount = cell * 3,
				});
			}
			return cells;
		}

		private static SoakGridCell WithMass(SoakGridCell source, float mass)
		{
			source.Mass = mass;
			return source;
		}

		private static SoakGridReconcileAckPacket CloneAck(SoakGridReconcileAckPacket source)
		{
			return new SoakGridReconcileAckPacket
			{
				RunId = source.RunId,
				SampleId = source.SampleId,
				Generation = source.Generation,
				WorldUpdateCut = source.WorldUpdateCut,
				ChunkIndex = source.ChunkIndex,
				TotalChunks = source.TotalChunks,
				TotalRecords = source.TotalRecords,
				ChunkHash = source.ChunkHash.ToArray(),
				FullGridHash = source.FullGridHash.ToArray(),
			};
		}

		private static T RoundTrip<T>(T source) where T : IPacket, new()
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				source.Serialize(writer);
			stream.Position = 0;
			var copy = new T();
			using var reader = new BinaryReader(stream);
			copy.Deserialize(reader);
			return copy;
		}
	}
}
#endif
