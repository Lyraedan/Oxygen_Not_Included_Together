using System;
using System.IO;
using System.IO.Compression;
using ONI_Together.Misc;
using ONI_Together.Misc.World;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.DuplicantActions;
using ONI_Together.Networking.Packets.Social;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class PacketBoundsTests
	{
		[UnitTest(name: "Cursor relay drops oversized utility paths atomically", category: "Networking")]
		public static UnitTestResult DropsOversizedCursorPathAtomically()
		{
			var oversized = new PlayerCursorPacket
			{
				PlayerID = 1,
				BuildingPrefabId = string.Empty,
				HasUtilityPath = true,
				UtilityPathData = new uint[500],
			};
			if (!HostBroadcastPacket.TryFitUnreliableRelay(oversized)
			    || oversized.HasUtilityPath || oversized.UtilityPathData != null
			    || HostBroadcastPacket.GetRelayWireSize(oversized)
			        > PacketSender.MAX_PACKET_SIZE_UNRELIABLE)
				return UnitTestResult.Fail(
					"Oversized cursor path was fragmented, truncated, or left above the datagram budget");

			var small = new PlayerCursorPacket
			{
				PlayerID = 1,
				BuildingPrefabId = string.Empty,
				HasUtilityPath = true,
				UtilityPathData = new uint[] { 1, 2 },
			};
			if (!HostBroadcastPacket.TryFitUnreliableRelay(small)
			    || !small.HasUtilityPath || small.UtilityPathData?.Length != 2)
				return UnitTestResult.Fail("A cursor path that fits the datagram budget was removed");
			return UnitTestResult.Pass(
				"Utility paths are preserved whole or cleared whole before unreliable relay");
		}

		[UnitTest(name: "Packet collections reject oversized counts", category: "Networking")]
		public static UnitTestResult RejectsOversizedCounts()
		{
			if (!Rejects(new PlayAnimPacket(), writer =>
			{
				writer.Write(0); writer.Write(0L); writer.Write(0); writer.Write(1f); writer.Write(0f); writer.Write(false);
				writer.Write(PlayAnimPacket.MaxAnimCount + 1);
			})) return UnitTestResult.Fail("PlayAnimPacket accepted an oversized count");

			if (!Rejects(new PlayerCursorPacket(), writer =>
			{
				writer.Write(1UL);
				for (int i = 0; i < 3 + 4; i++) writer.Write(0f);
				writer.Write((ushort)(1 << 13)); writer.Write(0U); writer.Write(0U); writer.Write(string.Empty);
				writer.Write(PlayerCursorPacket.MaxUtilityPathCount + 1);
			})) return UnitTestResult.Fail("PlayerCursorPacket accepted an oversized path");

			if (!Rejects(new VitalStatsPacket(), writer =>
			{
				writer.Write(0); writer.Write((byte)0); writer.Write(0); writer.Write(VitalStatsPacket.MaxVitalCount + 1);
			})) return UnitTestResult.Fail("VitalStatsPacket accepted an oversized count");

			if (!Rejects(new ChatHistorySyncPacket(), writer => writer.Write(ChatHistorySyncPacket.MaxMessageCount + 1)))
				return UnitTestResult.Fail("ChatHistorySyncPacket accepted an oversized count");

			if (!Rejects(new DreamBubblePacket(), writer =>
			{
				writer.Write(0); writer.Write(false); writer.Write(string.Empty); writer.Write(string.Empty);
				writer.Write(DreamBubblePacket.MaxIconCount + 1);
			})) return UnitTestResult.Fail("DreamBubblePacket accepted an oversized count");

			if (!Rejects(new TrailPointsPacket(), writer =>
			{
				writer.Write(1UL); writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(false);
				writer.Write(TrailPointsPacket.MaxPointCount + 1);
			})) return UnitTestResult.Fail("TrailPointsPacket accepted an oversized count");

			if (!Rejects(new BuildingStatePacket(), writer => writer.Write(BuildingStatePacket.MaxBuildingCount + 1)))
				return UnitTestResult.Fail("BuildingStatePacket accepted an oversized count");
			if (!Rejects(new ChoreStatePacket(), writer => writer.Write(ChoreStatePacket.MaxChoreCount + 1)))
				return UnitTestResult.Fail("ChoreStatePacket accepted an oversized count");
			if (!Rejects(new ConduitContentsPacket(), writer => writer.Write(ConduitContentsPacket.MaxUpdateCount + 1)))
				return UnitTestResult.Fail("ConduitContentsPacket accepted an oversized count");
			if (!Rejects(new DiggingStatePacket(), writer => writer.Write(DiggingStatePacket.MaxCellCount + 1)))
				return UnitTestResult.Fail("DiggingStatePacket accepted an oversized count");
			if (!Rejects(new DisinfectStatePacket(), writer => writer.Write(DisinfectStatePacket.MaxCellCount + 1)))
				return UnitTestResult.Fail("DisinfectStatePacket accepted an oversized count");
			if (!Rejects(new PlantGrowthStatePacket(), writer =>
			{
				writer.Write(1UL);
				writer.Write(PlantGrowthStatePacket.MaxPlantCount + 1);
			}))
				return UnitTestResult.Fail("PlantGrowthStatePacket accepted an oversized count");
			if (!Rejects(new PrioritizeStatePacket(), writer => writer.Write(PrioritizeStatePacket.MaxPriorityCount + 1)))
				return UnitTestResult.Fail("PrioritizeStatePacket accepted an oversized count");
			if (!Rejects(new ResearchStatePacket(), writer => writer.Write(ResearchStatePacket.MaxTechCount + 1)))
				return UnitTestResult.Fail("ResearchStatePacket accepted an oversized count");

			return UnitTestResult.Pass("Collection counts are rejected before allocation");
		}

		[UnitTest(name: "Plant packets reject invalid lifecycle metadata", category: "Networking")]
		public static UnitTestResult RejectsInvalidPlantLifecycleMetadata()
		{
			if (!Rejects(new PlantGrowthStatePacket(), writer => writer.Write(0UL)))
				return UnitTestResult.Fail("PlantGrowthStatePacket accepted revision zero");
			if (!Rejects(new PlantLifecyclePacket(), writer => writer.Write((byte)2)))
				return UnitTestResult.Fail("PlantLifecyclePacket accepted an unknown operation");
			if (!Rejects(new PlantLifecyclePacket(), writer =>
			{
				writer.Write((byte)PlantLifecycleOperation.Spawn);
				WritePlantData(writer, 7, 0);
			})) return UnitTestResult.Fail("PlantLifecyclePacket accepted revision zero");
			if (!Rejects(new PlantGrowthStatePacket(), writer =>
			{
				writer.Write(9UL);
				writer.Write(2);
				WritePlantData(writer, 7, 8);
				WritePlantData(writer, 7, 8);
			})) return UnitTestResult.Fail("PlantGrowthStatePacket accepted duplicate NetIds");
			return UnitTestResult.Pass("Plant lifecycle metadata is bounded and unique");
		}

		[UnitTest(name: "Packet blobs require exact declared lengths", category: "Networking")]
		public static UnitTestResult RequiresExactBlobLengths()
		{
			if (!ThrowsEndOfStream(reader => StructureStatePacket.ReadOptionalValues(reader), writer =>
			{
				writer.Write(4);
				writer.Write((short)0);
			})) return UnitTestResult.Fail("StructureStatePacket accepted a truncated blob");

			if (!ThrowsEndOfStream(new LogicStatePacket(), writer =>
			{
				WriteLogicStatePrefix(writer);
				writer.Write(4);
				writer.Write((short)0);
			})) return UnitTestResult.Fail("LogicStatePacket accepted a truncated blob");

			if (!ThrowsEndOfStream(new InstantiationsPacket(), writer =>
			{
				writer.Write(4);
				writer.Write((short)0);
			})) return UnitTestResult.Fail("InstantiationsPacket accepted a truncated payload");

			if (!ThrowsEndOfStream(new WorldDataPacket(), writer =>
			{
				writer.Write(4);
				writer.Write((short)0);
			})) return UnitTestResult.Fail("WorldDataPacket accepted a truncated payload");

			return UnitTestResult.Pass("Declared blob lengths require exact reads");
		}

		[UnitTest(name: "Compressed packets reject expansion bombs", category: "Networking")]
		public static UnitTestResult RejectsExpansionBomb()
		{
			byte[] bomb;
			using (var compressed = new MemoryStream())
			{
				using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, true))
				{
					byte[] zeros = new byte[NavigatorPathPacket.MaxDecompressedBytes + 1];
					gzip.Write(zeros, 0, zeros.Length);
				}
				bomb = compressed.ToArray();
			}

			if (!Rejects(new NavigatorPathPacket(), writer =>
			{
				writer.Write(bomb.Length);
				writer.Write(bomb);
			})) return UnitTestResult.Fail("NavigatorPathPacket accepted an expansion bomb");

			return UnitTestResult.Pass("Compressed output is bounded during decompression");
		}

		[UnitTest(name: "World data validates chunk sizes before allocation", category: "Networking")]
		public static UnitTestResult ValidatesWorldChunkBeforeAllocation()
		{
			byte[] payload = Deflate(writer =>
			{
				writer.Write(1);
				writer.Write(0); writer.Write(0); writer.Write(32); writer.Write(32);
				writer.Write(0); writer.Write(0); writer.Write(32); writer.Write(32);
				writer.Write(int.MaxValue);
			});

			if (!Rejects(new WorldDataPacket(), writer =>
			{
				writer.Write(payload.Length);
				writer.Write(payload);
			})) return UnitTestResult.Fail("WorldDataPacket allocated from an invalid chunk cell count");

			return UnitTestResult.Pass("World chunk dimensions are validated before array allocation");
		}

		[UnitTest(name: "Bounded compressed packets preserve valid payloads", category: "Networking")]
		public static UnitTestResult CompressedPacketRoundTrips()
		{
			if (!SnapshotWireBoundsTests.TryGetValidCell(out int cell))
				return UnitTestResult.Skip("Structure roundtrip requires an initialized world grid");

			var path = RoundTrip(new NavigatorPathPacket
			{
				NetId = 7,
				Steps =
				[
					new NavigatorPathPacket.PathStep { Cell = 42, NavType = default, TransitionId = 3 }
				]
			});
			if (path.NetId != 7 || path.Steps.Count != 1 || path.Steps[0].Cell != 42)
				return UnitTestResult.Fail("NavigatorPathPacket roundtrip changed valid data");

			var instantiations = RoundTrip(new InstantiationsPacket
			{
				Entries =
				[
					new InstantiationsPacket.InstantiationEntry
					{
						PrefabName = "TestPrefab", ObjectName = "TestObject", InitializeId = true, GameLayer = 2
					}
				]
			});
			if (instantiations.Entries.Count != 1 || instantiations.Entries[0].PrefabName != "TestPrefab")
				return UnitTestResult.Fail("InstantiationsPacket roundtrip changed valid data");

			var world = RoundTrip(new WorldDataPacket
			{
				SnapshotGeneration = 1,
				IsFinalChunk = true,
				ChunkIndex = 0,
				ChunkCount = 1,
				GridChunkCount = 1,
				Chunks =
				[
					new ChunkData
					{
						TileX = 1, TileY = 2, Width = 1, Height = 1,
						Tiles = [3], Temperatures = [290f], Masses = [1f], DiseaseIdx = [0], DiseaseCount = [0]
					}
				]
			});
			if (world.Chunks.Count != 1 || world.Chunks[0].Tiles.Length != 1 || world.Chunks[0].Tiles[0] != 3)
				return UnitTestResult.Fail("WorldDataPacket roundtrip changed valid data");

			var structure = RoundTrip(new StructureStatePacket
			{
				NetId = -8,
				Cell = cell,
				Value = (Variant)42,
				OptionalValues = new System.Collections.Generic.Dictionary<string, Variant>
				{
					["blob"] = (Variant)new byte[] { 1, 2, 3 }
				}
			});
			if (structure.NetId != -8 || structure.Value.Int != 42
			    || structure.OptionalValues["blob"].ByteArray.Length != 3)
				return UnitTestResult.Fail("StructureStatePacket roundtrip changed signed identity or valid variants");

			var logic = RoundTrip(new LogicStatePacket
			{
				NetId = 9,
				Value = (Variant)"logic",
				OptionalValues = new System.Collections.Generic.Dictionary<string, Variant>()
			});
			if (logic.Value.String != "logic" || logic.OptionalValues.Count != 0)
				return UnitTestResult.Fail("LogicStatePacket roundtrip changed valid variants");

			return UnitTestResult.Pass("Valid compressed payloads still roundtrip");
		}

		private static void WriteLogicStatePrefix(BinaryWriter writer)
		{
			writer.Write(0);
			writer.Write(0);
			WriteVariantAndActivePrefix(writer);
		}

		private static void WriteVariantAndActivePrefix(BinaryWriter writer)
		{
			writer.Write((byte)Variant.TypeCode.Int);
			writer.Write(0);
			writer.Write(false);
		}

		private static void WritePlantData(BinaryWriter writer, int netId, ulong revision)
		{
			writer.Write(netId);
			writer.Write(revision);
			writer.Write(0);
			writer.Write(1);
			writer.Write("MealLice");
			writer.Write(0.5f);
			writer.Write(false);
			writer.Write(false);
			writer.Write(true);
		}

		private static byte[] Deflate(Action<BinaryWriter> write)
		{
			using var output = new MemoryStream();
			using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
			using (var writer = new BinaryWriter(deflate))
				write(writer);
			return output.ToArray();
		}

		private static bool Rejects(IPacket packet, Action<BinaryWriter> write)
		{
			try
			{
				Deserialize(packet, write);
				return false;
			}
			catch (InvalidDataException)
			{
				return true;
			}
		}

		private static bool ThrowsEndOfStream(IPacket packet, Action<BinaryWriter> write)
			=> ThrowsEndOfStream(packet.Deserialize, write);

		private static bool ThrowsEndOfStream(Action<BinaryReader> read, Action<BinaryWriter> write)
		{
			try
			{
				using var stream = new MemoryStream();
				using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
					write(writer);
				stream.Position = 0;
				using var reader = new BinaryReader(stream);
				read(reader);
				return false;
			}
			catch (EndOfStreamException)
			{
				return true;
			}
		}

		private static void Deserialize(IPacket packet, Action<BinaryWriter> write)
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				write(writer);
			stream.Position = 0;
			using var reader = new BinaryReader(stream);
			packet.Deserialize(reader);
		}

		private static T RoundTrip<T>(T input) where T : IPacket, new()
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				input.Serialize(writer);
			stream.Position = 0;
			var output = new T();
			using var reader = new BinaryReader(stream);
			output.Deserialize(reader);
			return output;
		}
	}
}
