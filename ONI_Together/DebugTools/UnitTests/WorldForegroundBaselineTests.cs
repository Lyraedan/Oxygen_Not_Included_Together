#if DEBUG
using System.Collections.Generic;
using System.IO;
using ONI_Together.Misc.World;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class WorldForegroundBaselineTests
	{
		[UnitTest(name: "World baseline carries exact update cuts and rejects delayed repair", category: "Networking")]
		public static UnitTestResult BaselineCarriesWorldUpdateCuts()
		{
			var packet = new WorldDataPacket
			{
				SnapshotGeneration = 7,
				IsFinalChunk = true,
				ChunkIndex = 0,
				ChunkCount = 1,
				GridChunkCount = 1,
				Chunks = new List<ChunkData> { CreateSingleCellChunk() },
				WorldUpdateForegroundBaseline = 77,
				WorldUpdateRevisionBaseline = 91,
				WorldUpdateRepairSequenceBaseline = 63,
			};
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				packet.Serialize(writer);
			stream.Position = 0;
			var copy = new WorldDataPacket();
			using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
				copy.Deserialize(reader);
			if (copy.WorldUpdateForegroundBaseline != 77
			    || copy.WorldUpdateRevisionBaseline != 91
			    || copy.WorldUpdateRepairSequenceBaseline != 63)
				return UnitTestResult.Fail("Final world baseline lost a world-update cut");

			WorldUpdatePacket.ResetRevisionState();
			try
			{
				WorldUpdatePacket.AdvanceClientSupersededRevision(91);
				WorldUpdatePacket.SetClientForegroundBaseline(77);
				WorldUpdatePacket.SetClientRepairBaseline(63);
				if (WorldUpdatePacket.TryAcceptForegroundSequence(79)
				    || !WorldUpdatePacket.TryAcceptForegroundSequence(78))
					return UnitTestResult.Fail("Post-baseline foreground stream admitted a gap");
				if (WorldUpdatePacket.ShouldApply(false, true, 91, WorldUpdatePacket.ClientSupersededRevision)
				    || !WorldUpdatePacket.ShouldApply(false, true, 92, WorldUpdatePacket.ClientSupersededRevision))
					return UnitTestResult.Fail("A delayed pre-baseline repair overwrote the snapshot");
				if (WorldUpdatePacket.ClientResolvedRepairSequence != 63)
					return UnitTestResult.Fail("Repair delivery proof did not start at the snapshot cut");
			}
			finally
			{
				WorldUpdatePacket.ResetRevisionState();
			}
			return UnitTestResult.Pass("Baseline binds foreground order and rejects delayed repairs at its revision cut");
		}

		[UnitTest(name: "Non-final world chunks cannot move update cuts", category: "Networking")]
		public static UnitTestResult NonFinalChunkCannotCarryUpdateCuts()
		{
			try
			{
				var packet = new WorldDataPacket
				{
					SnapshotGeneration = 7,
					IsFinalChunk = false,
					ChunkIndex = 0,
					ChunkCount = 2,
					GridChunkCount = 2,
					Chunks = new List<ChunkData> { CreateSingleCellChunk() },
					WorldUpdateForegroundBaseline = 1,
					WorldUpdateRevisionBaseline = 1,
					WorldUpdateRepairSequenceBaseline = 1,
				};
				using var stream = new MemoryStream();
				using var writer = new BinaryWriter(stream);
				packet.Serialize(writer);
				return UnitTestResult.Fail("A partial world chunk advanced the foreground cut");
			}
			catch (InvalidDataException)
			{
				return UnitTestResult.Pass("Only the exact final baseline can initialize foreground order");
			}
		}

		[UnitTest(name: "World lifecycle baseline is paged inside the ACK window", category: "Networking")]
		public static UnitTestResult LifecycleBaselineIsPagedAndWireBounded()
		{
			const int lifecycleCount = 3800;
			var lifecycle = new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>(lifecycleCount);
			for (int netId = 1; netId <= lifecycleCount; netId++)
				lifecycle.Add(LiveLifecycle(netId, (ulong)netId));

			List<WorldDataPacket> packets = WorldDataRequestPacket.BuildPacketsForTests(
				generation: 44,
				chunks: new[] { CreateFullChunk() },
				lifecycle: lifecycle,
				foregroundCut: 11,
				revisionCut: 12,
				repairCut: 13);
			int lifecyclePages = (lifecycleCount + WorldDataPacket.MaxLifecycleEntriesPerPacket - 1)
			                     / WorldDataPacket.MaxLifecycleEntriesPerPacket;
			if (packets.Count != lifecyclePages)
				return UnitTestResult.Fail("Lifecycle metadata was retained in one oversized final packet");

			int observedEntries = 0;
			for (int index = 0; index < packets.Count; index++)
			{
				WorldDataPacket packet = packets[index];
				if (packet.ChunkIndex != index || packet.ChunkCount != packets.Count
				    || packet.GridChunkCount != 1
				    || packet.LifecycleBaselineTotalEntries != lifecycleCount)
					return UnitTestResult.Fail("Paged baseline lost generation-local ordering metadata");
				if (index == 0 && packet.Chunks.Count != 1
				    || index > 0 && packet.Chunks.Count != 0
				    || packet.LifecycleBaseline.Count <= 0
				    || packet.LifecycleBaseline.Count > WorldDataPacket.MaxLifecycleEntriesPerPacket)
					return UnitTestResult.Fail("Grid and lifecycle pages did not retain their bounded packet shapes");

				observedEntries += packet.LifecycleBaseline.Count;
				int fragments = ReliableFragments(packet);
				if (fragments > WorldDataPacket.MaxReliableFragmentsPerPacket)
					return UnitTestResult.Fail($"Baseline packet expanded to {fragments} reliable fragments");
				bool final = index == packets.Count - 1;
				if (packet.IsFinalChunk != final
				    || !final && (packet.WorldUpdateForegroundBaseline != 0
				                  || packet.WorldUpdateRevisionBaseline != 0
				                  || packet.WorldUpdateRepairSequenceBaseline != 0))
					return UnitTestResult.Fail("Update cuts escaped the complete final lifecycle page");
			}

			int maxWindowFragments = WorldDataSendWindow.MaxInFlightChunks
			                         * WorldDataPacket.MaxReliableFragmentsPerPacket;
			return observedEntries == lifecycleCount
			       && maxWindowFragments == WorldDataPacket.MaxInFlightReliableFragments
			       && maxWindowFragments < WorldDataPacket.ReliableAckHistoryMessages
				? UnitTestResult.Pass("Lifecycle pages share a bounded 12-fragment ACK window")
				: UnitTestResult.Fail("Lifecycle paging lost entries or its transport-fragment bound");
		}

		[UnitTest(name: "World lifecycle pages reject duplicates and incomplete final state", category: "Networking")]
		public static UnitTestResult LifecyclePagesRequireExactCompleteMembership()
		{
			var firstPage = new[]
			{
				LiveLifecycle(1, 1),
				LiveLifecycle(2, 2),
			};
			var duplicatePage = new[]
			{
				new NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry(2, 3, true),
			};
			var collector = new WorldDataLifecycleCollector(expectedEntries: 3);
			if (!collector.TryAppend(firstPage, isFinalPage: false)
			    || collector.TryAppend(duplicatePage, isFinalPage: true))
				return UnitTestResult.Fail("Cross-page duplicate lifecycle NetId was accepted");

			var incomplete = new WorldDataLifecycleCollector(expectedEntries: 3);
			if (incomplete.TryAppend(firstPage, isFinalPage: true))
				return UnitTestResult.Fail("Final lifecycle page committed with a missing entry");

			var complete = new WorldDataLifecycleCollector(expectedEntries: 2);
			return complete.TryAppend(firstPage, isFinalPage: true) && complete.IsComplete
				? UnitTestResult.Pass("Lifecycle completion requires an exact unique entry set")
				: UnitTestResult.Fail("Exact lifecycle pages did not complete");
		}

		[UnitTest(name: "World lifecycle baseline carries exact live descriptors", category: "Networking")]
		public static UnitTestResult LifecycleDescriptorRoundtrip()
		{
			NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry live = LiveLifecycle(7, 11);
			var tombstone = new NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry(8, 12, true);
			var packet = new WorldDataPacket
			{
				SnapshotGeneration = 9,
				IsFinalChunk = true,
				ChunkIndex = 0,
				ChunkCount = 1,
				GridChunkCount = 1,
				LifecycleBaselineTotalEntries = 2,
				Chunks = new List<ChunkData> { CreateSingleCellChunk() },
				LifecycleBaseline = new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>
				{
					live, tombstone,
				},
			};
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				packet.Serialize(writer);
			stream.Position = 0;
			var copy = new WorldDataPacket();
			using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
				copy.Deserialize(reader);
			SpawnPrefabPacket descriptor = copy.LifecycleBaseline[0].Descriptor;
			return descriptor != null && descriptor.NetId == 7 && descriptor.Revision == 11
			       && descriptor.Hash == 7007 && descriptor.WorldId == 3
			       && descriptor.Position == new UnityEngine.Vector3(7f, 2f, 0f)
			       && copy.LifecycleBaseline[1].Tombstoned
			       && copy.LifecycleBaseline[1].Descriptor == null
				? UnitTestResult.Pass("Live lifecycle descriptor and tombstone shape roundtrip exactly")
				: UnitTestResult.Fail("Lifecycle descriptor or tombstone shape changed on the wire");
		}

		[UnitTest(name: "World lifecycle transfer rejects missing or mismatched descriptors", category: "Networking")]
		public static UnitTestResult LifecycleDescriptorIsRequiredForLiveEntries()
		{
			var missing = new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>
			{
				new(1, 1, false),
			};
			var mismatched = new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>
			{
				new(2, 2, false, Descriptor(netId: 2, revision: 3)),
			};
			var tombstoneWithDescriptor = new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>
			{
				new(3, 3, true, Descriptor(netId: 3, revision: 3)),
			};
			IReadOnlyList<ChunkData> chunks = new[] { CreateSingleCellChunk() };
			bool rejectsMissing = WorldDataRequestPacket.BuildPacketsForTests(
				1, chunks, missing, 0, 0, 0).Count == 0;
			bool rejectsMismatch = WorldDataRequestPacket.BuildPacketsForTests(
				1, chunks, mismatched, 0, 0, 0).Count == 0;
			bool rejectsTombstonePayload = WorldDataRequestPacket.BuildPacketsForTests(
				1, chunks, tombstoneWithDescriptor, 0, 0, 0).Count == 0;
			return rejectsMissing && rejectsMismatch && rejectsTombstonePayload
				? UnitTestResult.Pass("Lifecycle transfer requires one exact descriptor for every live entry")
				: UnitTestResult.Fail("Malformed lifecycle descriptor shape entered a world baseline");
		}

		private static int ReliableFragments(WorldDataPacket packet)
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				packet.Serialize(writer);
			long orderedWireBytes = stream.Length + WorldDataPacket.OrderedReliableEnvelopeBytes;
			return checked((int)((orderedWireBytes + WorldDataPacket.ReliableFragmentPayloadBytes - 1)
			                     / WorldDataPacket.ReliableFragmentPayloadBytes));
		}

		private static ChunkData CreateFullChunk()
		{
			const int side = 16;
			const int cells = side * side;
			var chunk = new ChunkData
			{
				TileX = 0, TileY = 0, Width = side, Height = side,
				Tiles = new ushort[cells], Temperatures = new float[cells],
				Masses = new float[cells], DiseaseIdx = new byte[cells],
				DiseaseCount = new int[cells],
			};
			uint state = 0x9e3779b9;
			for (int index = 0; index < cells; index++)
			{
				state = state * 1664525 + 1013904223;
				chunk.Tiles[index] = (ushort)state;
				chunk.Temperatures[index] = System.BitConverter.Int32BitsToSingle((int)(state | 0x3f000000));
				state = state * 1664525 + 1013904223;
				chunk.Masses[index] = System.BitConverter.Int32BitsToSingle((int)(state | 0x3f000000));
				chunk.DiseaseIdx[index] = (byte)(state >> 24);
				chunk.DiseaseCount[index] = (int)state;
			}
			return chunk;
		}

		private static ChunkData CreateSingleCellChunk()
			=> new()
			{
				TileX = 0, TileY = 0, Width = 1, Height = 1,
				Tiles = new ushort[] { 1 }, Temperatures = new float[] { 290f },
				Masses = new float[] { 1f }, DiseaseIdx = new byte[] { 0 },
				DiseaseCount = new int[] { 0 },
			};

		private static NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry LiveLifecycle(
			int netId, ulong revision)
			=> new(netId, revision, false, Descriptor(netId, revision));

		private static SpawnPrefabPacket Descriptor(int netId, ulong revision)
			=> new(netId, 7000 + netId, new UnityEngine.Vector3(netId, 2f, 0f))
			{
				Revision = revision,
				WorldId = 3,
				IsActive = true,
			};
	}
}
#endif
