using System;
using System.Collections.Generic;
using ONI_Together.Misc.World;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class WorldAuthoritativeSweepTests
	{
		[UnitTest(name: "Background world sweep emits unchanged host cells", category: "Networking")]
		public static UnitTestResult EmitsUnchangedHostCells()
		{
			var unchanged = new WorldUpdatePacket.CellUpdate
			{
				Cell = 17,
				ElementIdx = 3,
				Mass = 1.25f,
				Temperature = 295f,
				DiseaseIdx = 4,
				DiseaseCount = 12
			};
			if (!WorldStateSyncer.ShouldQueueCell(true, unchanged, unchanged))
				return UnitTestResult.Fail("Authoritative sweep skipped an unchanged host cell");
			if (WorldStateSyncer.ShouldQueueCell(false, unchanged, unchanged))
				return UnitTestResult.Fail("Delta viewport scan emitted an unchanged cell");
			var changed = unchanged;
			changed.Mass += 1f;
			if (!WorldStateSyncer.ShouldQueueCell(false, unchanged, changed))
				return UnitTestResult.Fail("Delta viewport scan skipped a changed cell");

			return UnitTestResult.Pass("Only authoritative sweeps emit unchanged host cells");
		}

		[UnitTest(name: "Background world sweep covers partial edges exactly once", category: "Networking")]
		public static UnitTestResult CoversPartialEdgesExactlyOnce()
		{
			const int width = 33;
			const int height = 34;
			var seen = new HashSet<int>();
			int chunkCount = WorldStateSyncer.BackgroundChunkCount(width, height);
			if (chunkCount != 4)
				return UnitTestResult.Fail("Partial-edge map did not produce four chunks");
			for (int chunk = 0; chunk < chunkCount; chunk++)
			{
				var bounds = WorldStateSyncer.BackgroundChunkBounds(width, height, chunk);
				string error = AddChunkCells(width, bounds, seen);
				if (error != null)
					return UnitTestResult.Fail(error);
			}

			if (seen.Count != 1122 || !seen.Contains(1121))
				return UnitTestResult.Fail("Background chunks omitted a valid partial-edge cell");
			return UnitTestResult.Pass("All valid cells are emitted once, including partial edges");
		}

		[UnitTest(name: "Background world sweep budgets a complete thirty-second pass", category: "Networking")]
		public static UnitTestResult SweepBudgetMeetsTargetWindow()
		{
			if (WorldStateSyncer.BackgroundChunksPerPass(260, 1.5f) != 13
			    || WorldStateSyncer.BackgroundChunksPerPass(260, 6f) != 52
			    || WorldStateSyncer.BackgroundChunksPerPass(4, 1.5f) != 1
			    || WorldStateSyncer.BackgroundChunksPerPass(0, 1.5f) != 0)
			{
				return UnitTestResult.Fail("Background authoritative sweep cannot finish in its target window");
			}

			return UnitTestResult.Pass("Chunk budget completes each authoritative pass in about thirty seconds");
		}

		[UnitTest(name: "Background world repair stays outside reliable loading journal", category: "Networking")]
		public static UnitTestResult BackgroundRepairDoesNotFloodLoadingJournal()
		{
			if (!WorldStateSyncer.ShouldUseBackgroundRepair(authoritative: true, changed: false)
			    || WorldStateSyncer.ShouldUseBackgroundRepair(authoritative: true, changed: true)
			    || WorldStateSyncer.ShouldUseBackgroundRepair(authoritative: false, changed: true))
				return UnitTestResult.Fail("Changed deltas and periodic repairs use the wrong delivery channel");

			WorldUpdateBatcher.ResetSessionState();
			WorldUpdateBatcher.QueueForTests(new WorldUpdatePacket.CellUpdate { Cell = 1 });
			WorldUpdateBatcher.QueueForTests(
				new WorldUpdatePacket.CellUpdate { Cell = 2 }, backgroundRepair: true);
			bool separated = WorldUpdateBatcher.PendingCountForTests(backgroundRepair: false) == 1
			                 && WorldUpdateBatcher.PendingCountForTests(backgroundRepair: true) == 1;
			WorldUpdateBatcher.ResetSessionState();
			return separated
				? UnitTestResult.Pass("Full repair is unreliable; changed cells remain reliably journalled")
				: UnitTestResult.Fail("Background repair was mixed into the reliable loading journal");
		}

		[UnitTest(name: "Soak suppresses only unchanged authoritative repairs", category: "Networking")]
		public static UnitTestResult SoakKeepsChangedAuthoritativeDeltas()
		{
			var unchanged = new WorldUpdatePacket.CellUpdate
			{
				Cell = 17,
				ElementIdx = 3,
				Mass = 1.25f,
				Temperature = 295f,
			};
			var changed = unchanged;
			changed.Mass += 1f;
			WorldStateSyncer.SetAuthoritativeRepairSuppressed(true);
			try
			{
				if (!WorldStateSyncer.AuthoritativeRepairSuppressedForTests
				    || WorldStateSyncer.ShouldQueueAuthoritativeSweepCell(
					    true, unchanged, unchanged)
				    || !WorldStateSyncer.ShouldQueueAuthoritativeSweepCell(
					    true, unchanged, changed))
					return UnitTestResult.Fail(
						"Soak suppression dropped a changed offscreen cell or retained repair traffic");
				return UnitTestResult.Pass(
					"Changed offscreen cells remain reliable while unchanged repairs are suppressed");
			}
			finally
			{
				WorldStateSyncer.SetAuthoritativeRepairSuppressed(false);
			}
		}

		[UnitTest(name: "Soak checkpoint sweep queues every changed cell only", category: "Networking")]
		public static UnitTestResult SoakCheckpointSweepIsDeltaOnly()
		{
			var unchanged = new WorldUpdatePacket.CellUpdate
			{
				Cell = 91,
				ElementIdx = 5,
				Mass = 2.5f,
				Temperature = 301f,
				DiseaseIdx = 2,
				DiseaseCount = 7,
			};
			var changed = unchanged;
			changed.Temperature += 1f;
			if (WorldStateSyncer.ShouldQueueCheckpointCell(unchanged, unchanged)
			    || !WorldStateSyncer.ShouldQueueCheckpointCell(unchanged, changed))
				return UnitTestResult.Fail(
					"Checkpoint sweep did not cover changed offscreen cells exactly");

			return UnitTestResult.Pass(
				"Checkpoint sweep is a complete changed-cell delta pass");
		}

		[UnitTest(name: "Authoritative repair dispatch drains a full world sweep", category: "Networking")]
		public static UnitTestResult RepairDispatchDrainsFullSweep()
		{
			const int totalCells = 257000;
			int perFrameBudget = WorldUpdateBatcher.DispatchBudgetForFrame(1f / 30f);
			if (perFrameBudget * 30 < 160 || perFrameBudget > 64
			    || WorldUpdateBatcher.DispatchBudgetForFrame(1f) != 64
			    || WorldUpdateBatcher.RepairStagingCapacity(totalCells) < totalCells)
				return UnitTestResult.Fail("Repair dispatch budget misses its rate or frame cap");

			int maxPackets = WorldUpdateBatcher.MaxRepairDispatchPacketsForTests;
			int cellsPerPacket = WorldUpdateBatcher.MaxRepairUpdatesPerPacketForTests;
			int queuedCells = (maxPackets + 2) * cellsPerPacket;
			WorldUpdateBatcher.ResetSessionState();
			try
			{
				for (int cell = 0; cell < queuedCells; cell++)
					WorldUpdateBatcher.QueueForTests(
						new WorldUpdatePacket.CellUpdate { Cell = cell }, backgroundRepair: true);
				WorldUpdateBatcher.PackagePendingForTests();
				int stagedBefore = WorldUpdateBatcher.PendingCountForTests(backgroundRepair: true);
				if (WorldUpdateBatcher.PendingRepairDispatchCountForTests != maxPackets
				    || stagedBefore != 2 * cellsPerPacket
				    || !WorldUpdateBatcher.TryTakePendingDispatch(
					    out _, out _, requireReadyClients: false))
					return UnitTestResult.Fail("Repair dispatch queue did not apply bounded backpressure");
				WorldUpdateBatcher.RefillDispatchQueuesForTests();
				bool refilled = WorldUpdateBatcher.PendingRepairDispatchCountForTests == maxPackets
				                && WorldUpdateBatcher.PendingCountForTests(backgroundRepair: true)
				                == stagedBefore - cellsPerPacket;
				return refilled
					? UnitTestResult.Pass("Repair dispatch refills continuously at the bounded send rate")
					: UnitTestResult.Fail("Repair dispatch queue did not refill after capacity became available");
			}
			finally
			{
				WorldUpdateBatcher.ResetSessionState();
			}
		}

		[UnitTest(name: "Low FPS authoritative sweep obeys repair pipeline headroom", category: "Networking")]
		public static UnitTestResult LowFpsSweepObeysRepairPipelineHeadroom()
		{
			const int totalCells = 257000;
			int requested = WorldStateSyncer.BackgroundChunksPerPass(260, 6f) * 32 * 32;
			int cellsPerPacket = WorldUpdateBatcher.MaxRepairUpdatesPerPacketForTests;
			int observationPackets = WorldUpdateRepairJournal.DefaultMaxEntries;
			WorldUpdateBatcher.ResetSessionState();
			try
			{
				int initialBudget = WorldUpdateBatcher.RepairProducerCellBudgetForTests(
					requested, totalCells);
				if (initialBudget <= 0 || initialBudget > observationPackets * cellsPerPacket
				    || initialBudget >= requested)
					return UnitTestResult.Fail("Low FPS producer ignored the bounded observation window");

				int queuedCells = (observationPackets - 1) * cellsPerPacket;
				for (int cell = 0; cell < queuedCells; cell++)
					WorldUpdateBatcher.QueueForTests(
						new WorldUpdatePacket.CellUpdate { Cell = cell }, backgroundRepair: true);
				WorldUpdateBatcher.PackagePendingForTests();
				int finalSlotBudget = WorldUpdateBatcher.RepairProducerCellBudgetForTests(
					requested, totalCells);
				if (finalSlotBudget <= 0 || finalSlotBudget > cellsPerPacket)
					return UnitTestResult.Fail("Producer budget did not shrink to the final packet slot");

				for (int cell = queuedCells; cell < queuedCells + finalSlotBudget; cell++)
					WorldUpdateBatcher.QueueForTests(
						new WorldUpdatePacket.CellUpdate { Cell = cell }, backgroundRepair: true);
				WorldUpdateBatcher.PackagePendingForTests();
				return WorldUpdateBatcher.RepairProducerCellBudgetForTests(requested, totalCells) == 0
					? UnitTestResult.Pass("Low FPS producer stops exactly at dispatch and journal headroom")
					: UnitTestResult.Fail("Producer admitted work beyond the client observation window");
			}
			finally
			{
				WorldUpdateBatcher.ResetSessionState();
			}
		}

		[UnitTest(name: "Authoritative sweep cursor waits for complete enqueue", category: "Networking")]
		public static UnitTestResult SweepCursorWaitsForCompleteEnqueue()
		{
			WorldStateSyncer.AdvanceBackgroundSweepPosition(
				7, 0, 64, 1024, 260, out int chunk, out int offset);
			if (chunk != 7 || offset != 64)
				return UnitTestResult.Fail("Partial chunk enqueue advanced the chunk cursor");
			WorldStateSyncer.AdvanceBackgroundSweepPosition(
				chunk, offset, 0, 1024, 260, out chunk, out offset);
			if (chunk != 7 || offset != 64)
				return UnitTestResult.Fail("Blocked enqueue changed authoritative sweep progress");
			WorldStateSyncer.AdvanceBackgroundSweepPosition(
				chunk, offset, 960, 1024, 260, out chunk, out offset);
			if (chunk != 8 || offset != 0)
				return UnitTestResult.Fail("Complete chunk enqueue did not advance exactly once");
			WorldStateSyncer.AdvanceBackgroundSweepPosition(
				259, 0, 1024, 1024, 260, out chunk, out offset);
			return chunk == 0 && offset == 0
				? UnitTestResult.Pass("Cursor retains partial work and wraps only after complete enqueue")
				: UnitTestResult.Fail("Completed sweep did not wrap to the first chunk");
		}

		private static string AddChunkCells(int width, RectInt bounds, HashSet<int> seen)
		{
			for (int y = bounds.yMin; y < bounds.yMax; y++)
			for (int x = bounds.xMin; x < bounds.xMax; x++)
			{
				int cell = y * width + x;
				if (cell < 0 || cell >= 1122)
					return "Background chunk emitted an invalid cell";
				if (!seen.Add(cell))
					return "Background chunks emitted a cell more than once";
			}
			return null;
		}
	}
}
