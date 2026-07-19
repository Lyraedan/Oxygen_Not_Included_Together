using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using ONI_Together.Misc.World;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class WorldUpdatePacketTests
	{
		[UnitTest(name: "World update rejects oversized compressed payload", category: "Networking")]
		public static UnitTestResult RejectsOversizedPayload()
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
			{
				writer.Write(1L);
				writer.Write(1L);
				writer.Write(0L);
				writer.Write(0L);
				writer.Write(WorldUpdatePacket.MaxCompressedBytes + 1);
			}
			stream.Position = 0;

			try
			{
				using var reader = new BinaryReader(stream);
				new WorldUpdatePacket().Deserialize(reader);
				return UnitTestResult.Fail("Oversized compressed payload was accepted");
			}
			catch (InvalidDataException)
			{
				return UnitTestResult.Pass("Oversized compressed payload is rejected before allocation");
			}
		}
		[UnitTest(name: "World update accepts only host state", category: "Networking")]
		public static UnitTestResult AcceptsOnlyHostState()
		{
			if (WorldUpdatePacket.ShouldApply(true, true, revision: 2, supersededRevision: 1)
				|| WorldUpdatePacket.ShouldApply(false, false, revision: 2, supersededRevision: 1)
				|| !WorldUpdatePacket.ShouldApply(false, true, revision: 2, supersededRevision: 1)
				|| WorldUpdatePacket.ShouldApply(false, true, revision: 1, supersededRevision: 1)
				|| WorldUpdatePacket.ShouldApply(false, true, revision: 0, supersededRevision: 0))
			{
				return UnitTestResult.Fail("World update authority gate is incorrect");
			}

			return UnitTestResult.Pass("Clients accept only host revisions newer than the exact-grid cut");
		}
		[UnitTest(name: "World update grid cut rejects late reliable packets", category: "Networking")]
		public static UnitTestResult GridCutRejectsLateReliablePackets()
		{
			WorldUpdatePacket.ResetRevisionState();
			try
			{
				long first = WorldUpdatePacket.NextHostRevision();
				long second = WorldUpdatePacket.NextHostRevision();
				WorldUpdatePacket.AdvanceClientSupersededRevision(first);
				WorldUpdatePacket.AdvanceClientSupersededRevision(0);
				if (WorldUpdatePacket.ClientSupersededRevision != first
				    || WorldUpdatePacket.ShouldApply(false, true, first, first)
				    || !WorldUpdatePacket.ShouldApply(false, true, second, first))
					return UnitTestResult.Fail("Grid cut decreased or admitted a late pre-snapshot update");
				return UnitTestResult.Pass("Grid cut permanently rejects old revisions and admits newer state");
			}
			finally
			{
				WorldUpdatePacket.ResetRevisionState();
			}
		}
		[UnitTest(name: "World foreground sequence allows one baseline jump", category: "Networking")]
		public static UnitTestResult ForegroundSequenceAllowsOneBaselineJump()
		{
			WorldUpdatePacket.ResetRevisionState();
			try
			{
				if (WorldUpdatePacket.AcceptForegroundSequence(40) != ForegroundSequenceResult.Accepted
				    || WorldUpdatePacket.AcceptForegroundSequence(39) != ForegroundSequenceResult.Superseded
				    || WorldUpdatePacket.AcceptForegroundSequence(42) != ForegroundSequenceResult.Gap
				    || WorldUpdatePacket.AcceptForegroundSequence(41) != ForegroundSequenceResult.Accepted
				    || WorldUpdatePacket.AcceptForegroundSequence(42) != ForegroundSequenceResult.Accepted
				    || WorldUpdatePacket.AcceptForegroundSequence(42) != ForegroundSequenceResult.Superseded)
					return UnitTestResult.Fail("Foreground sequence admitted a gap or rejected its first baseline jump");
				return UnitTestResult.Pass("First foreground packet establishes the baseline, then delivery is contiguous");
			}
			finally
			{
				WorldUpdatePacket.ResetRevisionState();
			}
		}

		[UnitTest(name: "World background repair obeys foreground cut and cell LWW", category: "Networking")]
		public static UnitTestResult BackgroundRepairObeysForegroundCutAndCellLww()
		{
			WorldUpdatePacket.ResetRevisionState();
			try
			{
				if (!WorldUpdatePacket.ShouldDeferRepair(40)
				    || !WorldUpdatePacket.TryAcceptForegroundSequence(40)
				    || WorldUpdatePacket.ShouldDeferRepair(40)
				    || !WorldUpdatePacket.ShouldDeferRepair(41))
					return UnitTestResult.Fail("Background repair crossed an unresolved foreground cut");

				if (!WorldUpdatePacket.TryAcceptCellRevision(7, 10, backgroundRepair: false)
				    || !WorldUpdatePacket.TryAcceptCellRevision(7, 10, backgroundRepair: false)
				    || WorldUpdatePacket.TryAcceptCellRevision(7, 9, backgroundRepair: true)
				    || !WorldUpdatePacket.TryAcceptCellRevision(7, 11, backgroundRepair: true)
				    || !WorldUpdatePacket.TryAcceptCellRevision(7, 11, backgroundRepair: true)
				    || WorldUpdatePacket.TryAcceptCellRevision(7, 10, backgroundRepair: true)
				    || !WorldUpdatePacket.TryAcceptCellRevision(8, 1, backgroundRepair: true))
					return UnitTestResult.Fail("Foreground operations were LWW-filtered or a stale repair won");

				return UnitTestResult.Pass("Foreground stays ordered; only absolute repair uses per-cell LWW after its cut");
			}
			finally
			{
				WorldUpdatePacket.ResetRevisionState();
			}
		}

		[UnitTest(name: "World pending background repair is bounded", category: "Networking")]
		public static UnitTestResult PendingBackgroundRepairIsBounded()
		{
			WorldUpdatePacket.ResetRevisionState();
			try
			{
				for (int i = 1; i <= WorldUpdatePacket.MaxPendingRepairPackets + 1; i++)
				{
					var packet = new WorldUpdatePacket
					{
						Revision = i,
						ForegroundCut = 1000,
						RepairSequence = i,
					};
					packet.Updates.Add(new WorldUpdatePacket.CellUpdate
					{
						Cell = i,
						ReplaceType = SimMessages.ReplaceType.Replace,
					});
					bool accepted = WorldUpdatePacket.DeferRepair(packet);
					if (accepted != (i <= WorldUpdatePacket.MaxPendingRepairPackets))
						return UnitTestResult.Fail("Deferred repair capacity did not report exact backpressure");
				}

				if (WorldUpdatePacket.PendingRepairPacketCount != WorldUpdatePacket.MaxPendingRepairPackets
				    || WorldUpdatePacket.PendingRepairUpdateCount > WorldUpdatePacket.MaxPendingRepairUpdates)
					return UnitTestResult.Fail("Unresolved repair exceeded its packet or update bound");
				return UnitTestResult.Pass("Deferred repair applies exact backpressure without eviction");
			}
			finally
			{
				WorldUpdatePacket.ResetRevisionState();
			}
		}

		[UnitTest(name: "World update preserves cell operation order and semantics", category: "Networking")]
		public static UnitTestResult PreservesCellOperationSemantics()
		{
			var packet = new WorldUpdatePacket { Revision = 1, Sequence = 7 };
			packet.Updates.Add(new WorldUpdatePacket.CellUpdate
			{
				Cell = 7, ElementIdx = 1, Temperature = 290f, Mass = 1f,
				ReplaceType = SimMessages.ReplaceType.None
			});
			packet.Updates.Add(new WorldUpdatePacket.CellUpdate
			{
				Cell = 7, ElementIdx = 2, Temperature = 291f, Mass = 2f,
				ReplaceType = SimMessages.ReplaceType.Replace
			});
			packet.Updates.Add(new WorldUpdatePacket.CellUpdate
			{
				Cell = 7, ElementIdx = 3, Temperature = 292f, Mass = 3f,
				ReplaceType = SimMessages.ReplaceType.ReplaceAndDisplace,
				DoVerticalSolidDisplacement = true
			});

			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				packet.Serialize(writer);
			stream.Position = 0;
			var copy = new WorldUpdatePacket();
			using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
				copy.Deserialize(reader);

			if (copy.Revision != 1 || copy.Sequence != 7 || copy.ForegroundCut != 0
				|| copy.RepairSequence != 0
				|| copy.Updates.Count != 3
				|| copy.Updates[0].ReplaceType != SimMessages.ReplaceType.None
				|| copy.Updates[1].ReplaceType != SimMessages.ReplaceType.Replace
				|| copy.Updates[2].ReplaceType != SimMessages.ReplaceType.ReplaceAndDisplace
				|| !copy.Updates[2].DoVerticalSolidDisplacement)
			{
				return UnitTestResult.Fail("Same-cell ModifyCell operations changed order or semantics");
			}

			return UnitTestResult.Pass("Same-cell ModifyCell operations retain host order and semantics");
		}

		[UnitTest(name: "World background repair roundtrip carries foreground cut", category: "Networking")]
		public static UnitTestResult BackgroundRepairRoundtripCarriesForegroundCut()
		{
			var packet = new WorldUpdatePacket
			{
				Revision = 9,
				ForegroundCut = 7,
				RepairSequence = 5,
			};
			packet.Updates.Add(new WorldUpdatePacket.CellUpdate
			{
				Cell = 12,
				Mass = 1f,
				Temperature = 290f,
				ReplaceType = SimMessages.ReplaceType.Replace,
			});

			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				packet.Serialize(writer);
			stream.Position = 0;
			var copy = new WorldUpdatePacket();
			using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
				copy.Deserialize(reader);

			if (!copy.IsBackgroundRepair || copy.Revision != 9
			    || copy.Sequence != 0 || copy.ForegroundCut != 7 || copy.RepairSequence != 5)
				return UnitTestResult.Fail("Repair lost its delivery class or foreground cut");
			packet.Updates[0] = new WorldUpdatePacket.CellUpdate
			{
				Cell = 12, ReplaceType = SimMessages.ReplaceType.ReplaceAndDisplace,
			};
			try
			{
				using var invalid = new MemoryStream();
				using var writer = new BinaryWriter(invalid);
				packet.Serialize(writer);
				return UnitTestResult.Fail("Repair admitted a non-idempotent displacement");
			}
			catch (InvalidDataException)
			{
				return UnitTestResult.Pass("Repair carries its cut and only permits idempotent replacement");
			}
		}

		[UnitTest(name: "World batcher queues one-at-a-time foreground-first dispatch", category: "Networking")]
		public static UnitTestResult BatcherQueuesForegroundFirstDispatch()
		{
			WorldUpdateBatcher.ResetSessionState();
			try
			{
				WorldUpdateBatcher.QueueForTests(new WorldUpdatePacket.CellUpdate
				{
					Cell = 7, ElementIdx = 1, ReplaceType = SimMessages.ReplaceType.None
				});
				WorldUpdateBatcher.QueueForTests(new WorldUpdatePacket.CellUpdate
				{
					Cell = 7, ElementIdx = 2, ReplaceType = SimMessages.ReplaceType.Replace
				});
				WorldUpdateBatcher.QueueForTests(new WorldUpdatePacket.CellUpdate
				{
					Cell = 8, ElementIdx = 3, ReplaceType = SimMessages.ReplaceType.Replace
				}, backgroundRepair: true);

				if (WorldUpdateBatcher.Flush() <= 0 || !WorldUpdateBatcher.HasPendingDispatch
				    || WorldUpdateBatcher.PendingCountForTests(false) != 0
				    || WorldUpdateBatcher.PendingCountForTests(true) != 0)
					return UnitTestResult.Fail("Flush sent immediately or failed to queue dispatch");

				if (!WorldUpdateBatcher.TryTakePendingDispatch(
					    out WorldUpdatePacket foreground, out PacketSendMode foregroundMode,
					    requireReadyClients: false)
				    || foregroundMode != PacketSendMode.Reliable || foreground.IsBackgroundRepair
				    || foreground.Updates.Count != 2
				    || foreground.Updates[0].ElementIdx != 1
				    || foreground.Updates[1].ElementIdx != 2
				    || !WorldUpdateBatcher.HasPendingDispatch)
					return UnitTestResult.Fail("Foreground operations lost order or dispatch priority");

				if (!WorldUpdateBatcher.TryTakePendingDispatch(
					    out WorldUpdatePacket repair, out PacketSendMode repairMode,
					    requireReadyClients: false)
				    || repairMode != PacketSendMode.Unreliable || !repair.IsBackgroundRepair
				    || repair.ForegroundCut != foreground.Sequence
				    || repair.Revision <= foreground.Revision
				    || repair.RepairSequence != 1
				    || WorldUpdateBatcher.HasPendingDispatch)
					return UnitTestResult.Fail("Repair did not carry a safe foreground cut or drained first");

				return UnitTestResult.Pass("Flush only queues; each dispatch is one packet with foreground first");
			}
			finally
			{
				WorldUpdateBatcher.ResetSessionState();
			}
		}

		[UnitTest(name: "World repair dispatch freezes at an exact sequence cut", category: "Networking")]
		public static UnitTestResult RepairDispatchFreezesAtExactCut()
		{
			WorldUpdateBatcher.ResetSessionState();
			try
			{
				if (!WorldUpdateBatcher.TryFreezeRepairDispatch(out long cut) || cut != 0
				    || !WorldUpdateBatcher.RepairDispatchPausedForTests)
					return UnitTestResult.Fail("Empty repair stream could not freeze at sequence zero");
				bool queuedWhileFrozen = WorldUpdateBatcher.QueueForTests(
					new WorldUpdatePacket.CellUpdate
				{
					Cell = 11,
					ReplaceType = SimMessages.ReplaceType.Replace,
				}, backgroundRepair: true);
				WorldUpdateBatcher.Flush();
				if (queuedWhileFrozen || WorldUpdateBatcher.TryTakePendingDispatch(
					    out _, out _, requireReadyClients: false))
					return UnitTestResult.Fail("A post-cut repair crossed the frozen hash boundary");
				WorldUpdateBatcher.ResumeRepairDispatch();
				if (!WorldUpdateBatcher.QueueForTests(new WorldUpdatePacket.CellUpdate
				    {
					    Cell = 11,
					    ReplaceType = SimMessages.ReplaceType.Replace,
				    }, backgroundRepair: true))
					return UnitTestResult.Fail("Repair did not become retryable after the checkpoint");
				WorldUpdateBatcher.Flush();
				if (!WorldUpdateBatcher.TryTakePendingDispatch(
					    out WorldUpdatePacket repair, out _, requireReadyClients: false)
				    || repair.RepairSequence != 1)
					return UnitTestResult.Fail("Resumed repair did not receive the next contiguous sequence");
				return UnitTestResult.Pass("Repairs after the cut wait until raw hashing finishes");
			}
			finally
			{
				WorldUpdateBatcher.ResetSessionState();
			}
		}

		[UnitTest(name: "World scan compares every hashed cell field exactly", category: "Networking")]
		public static UnitTestResult DetectsEveryHashedCellField()
		{
			var baseline = new WorldUpdatePacket.CellUpdate
			{
				Cell = 5, ElementIdx = 1, Mass = 1f, Temperature = 290f,
				DiseaseIdx = 2, DiseaseCount = 3
			};
			var changed = baseline;
			changed.Mass += 0.005f;
			if (!WorldStateSyncer.CellStateChanged(baseline, changed))
				return UnitTestResult.Fail("A 5g mass change was ignored");
			changed = baseline;
			changed.Temperature += 0.01f;
			if (!WorldStateSyncer.CellStateChanged(baseline, changed))
				return UnitTestResult.Fail("A temperature-only change was ignored");
			changed = baseline;
			changed.DiseaseIdx++;
			if (!WorldStateSyncer.CellStateChanged(baseline, changed))
				return UnitTestResult.Fail("A disease-index-only change was ignored");
			changed = baseline;
			changed.DiseaseCount++;
			if (!WorldStateSyncer.CellStateChanged(baseline, changed))
				return UnitTestResult.Fail("A disease-count-only change was ignored");
			if (WorldStateSyncer.CellStateChanged(baseline, baseline))
				return UnitTestResult.Fail("Identical cell state was reported as changed");

			return UnitTestResult.Pass("World scan uses the full exact grid hash domain");
		}

		[UnitTest(name: "World baseline covers partial edge chunks", category: "Networking")]
		public static UnitTestResult BaselineCoversPartialEdgeChunks()
		{
			if (WorldDataRequestPacket.ChunkCountForDimension(0) != 0
				|| WorldDataRequestPacket.ChunkCountForDimension(16) != 1
				|| WorldDataRequestPacket.ChunkCountForDimension(32) != 2
				|| WorldDataRequestPacket.ChunkCountForDimension(33) != 3)
			{
				return UnitTestResult.Fail("World baseline omitted a partial edge chunk");
			}

			if (WorldStateSyncer.BackgroundChunkCount(33, 33) != 4)
				return UnitTestResult.Fail("Background scan omitted partial edge chunks");

			return UnitTestResult.Pass("Baseline and background scans include partial edge cells");
		}

		[UnitTest(name: "World baseline binds completion to snapshot generation", category: "Networking")]
		public static UnitTestResult BaselineBindsSnapshotGeneration()
		{
			var packet = new WorldDataPacket
			{
				SnapshotGeneration = 17,
				IsFinalChunk = true,
				ChunkIndex = 2,
				ChunkCount = 3,
				GridChunkCount = 3,
				Chunks = new List<ChunkData> { CreateSingleCellChunk() },
			};
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				packet.Serialize(writer);
			stream.Position = 0;
			var copy = new WorldDataPacket();
			using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
				copy.Deserialize(reader);

			if (copy.SnapshotGeneration != 17 || !copy.IsFinalChunk
				|| copy.ChunkIndex != 2 || copy.ChunkCount != 3)
				return UnitTestResult.Fail("World baseline completion lost its snapshot generation");
			if (WorldDataRequestPacket.IsValidSnapshotGeneration(17, false))
				return UnitTestResult.Fail("World baseline request accepted a stale snapshot generation");

			return UnitTestResult.Pass("World baseline completion and requests are generation-bound");
		}

		[UnitTest(name: "World baseline final chunk carries lifecycle journal", category: "Networking")]
		public static UnitTestResult FinalChunkCarriesLifecycleJournal()
		{
			var packet = new WorldDataPacket
			{
				SnapshotGeneration = 18,
				WorldUpdateForegroundBaseline = 23,
				WorldUpdateRevisionBaseline = 29,
				WorldUpdateRepairSequenceBaseline = 31,
				IsFinalChunk = true,
				ChunkIndex = 0,
				ChunkCount = 1,
				GridChunkCount = 1,
				LifecycleBaselineTotalEntries = 2,
				Chunks = new List<ChunkData> { CreateSingleCellChunk() },
				LifecycleBaseline = new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>
				{
					new(-101, 20, false, new SpawnPrefabPacket
					{
						NetId = -101,
						Revision = 20,
						Hash = 101,
						Position = new UnityEngine.Vector3(1f, 2f, 0f),
						WorldId = 0,
						IsActive = true,
					}),
					new(-102, 21, true),
				}
			};
			WorldDataPacket copy = Roundtrip(packet);
			if (copy.WorldUpdateForegroundBaseline != 23
			    || copy.WorldUpdateRevisionBaseline != 29
			    || copy.WorldUpdateRepairSequenceBaseline != 31
			    || copy.LifecycleBaseline.Count != 2
			    || copy.LifecycleBaseline[0].NetId != -101
			    || copy.LifecycleBaseline[0].Revision != 20
			    || copy.LifecycleBaseline[0].Tombstoned
			    || copy.LifecycleBaseline[0].Descriptor?.NetId != -101
			    || copy.LifecycleBaseline[0].Descriptor.Hash != 101
			    || copy.LifecycleBaseline[1].NetId != -102
			    || copy.LifecycleBaseline[1].Revision != 21
			    || !copy.LifecycleBaseline[1].Tombstoned)
				return UnitTestResult.Fail("Final world baseline lost lifecycle journal state");

			return UnitTestResult.Pass("Lifecycle journal page roundtrips with its exact total");
		}

		[UnitTest(name: "World baseline rejects oversized lifecycle journal", category: "Networking")]
		public static UnitTestResult RejectsOversizedLifecycleJournal()
		{
			using MemoryStream stream = CreateLifecyclePayload(WorldDataPacket.MaxLifecycleBaselineEntries + 1);
			try
			{
				using var reader = new BinaryReader(stream);
				new WorldDataPacket().Deserialize(reader);
				return UnitTestResult.Fail("Oversized lifecycle journal was accepted");
			}
			catch (InvalidDataException)
			{
				return UnitTestResult.Pass("Oversized lifecycle journal is rejected before allocation");
			}
		}

		private static WorldDataPacket Roundtrip(WorldDataPacket packet)
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				packet.Serialize(writer);
			stream.Position = 0;
			var copy = new WorldDataPacket();
			using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
				copy.Deserialize(reader);
			return copy;
		}

		private static MemoryStream CreateLifecyclePayload(int lifecycleCount)
		{
			using var payload = new MemoryStream();
			using (var deflate = new DeflateStream(payload, CompressionLevel.Fastest, true))
			using (var writer = new BinaryWriter(deflate))
			{
				writer.Write(18L);
				writer.Write(true);
				writer.Write(0);
				writer.Write(1);
				writer.Write(1);
				writer.Write(lifecycleCount);
				writer.Write(0);
				writer.Write(0);
				writer.Write(0L);
				writer.Write(0L);
				writer.Write(0L);
			}
			var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
			{
				writer.Write(checked((int)payload.Length));
				writer.Write(payload.ToArray());
			}
			stream.Position = 0;
			return stream;
		}

		private static ChunkData CreateSingleCellChunk()
			=> new()
			{
				TileX = 0, TileY = 0, Width = 1, Height = 1,
				Tiles = new ushort[] { 1 }, Temperatures = new float[] { 290f },
				Masses = new float[] { 1f }, DiseaseIdx = new byte[] { 0 },
				DiseaseCount = new int[] { 0 },
			};
	}
}
