using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SnapshotWireBoundsTests
	{
		[UnitTest(name: "Save fallback: UDP chunks and transfer tracking are bounded", category: "Networking")]
		public static UnitTestResult SaveFallbackBounds()
		{
			if (SaveFileChunkPacket.MaxChunkBytes != 16 * 1024)
				return UnitTestResult.Fail("UDP save chunks are not capped at 16 KiB");
			if (!RejectsSaveMetadata(SaveFileChunkPacket.MaxChunkBytes + 1))
				return UnitTestResult.Fail("SaveFileChunkPacket accepted a chunk above the UDP cap");
			if (!RejectsSaveMetadata(1024))
				return UnitTestResult.Fail("SaveFileChunkPacket accepted a chunk outside the fixed UDP contract");
			if (!RejectsSaveMetadata(0, 2 * SaveFileChunkPacket.MaxChunkBytes,
				    SaveFileChunkPacket.MaxChunkBytes - 1)
			    || !RejectsSaveMetadata(SaveFileChunkPacket.MaxChunkBytes,
				    SaveFileChunkPacket.MaxChunkBytes + 123, 122))
				return UnitTestResult.Fail("SaveFileChunkPacket accepted a short middle or tail chunk");
			try
			{
				SaveFileChunkPacket.ValidateMetadata(
					SaveFileChunkPacket.MaxChunkBytes, SaveFileChunkPacket.MaxChunkBytes + 123,
					SaveFileChunkPacket.MaxChunkBytes, 123);
			}
			catch (InvalidDataException ex)
			{
				return UnitTestResult.Fail("SaveFileChunkPacket rejected a complete tail chunk: " + ex.Message);
			}

			int maxChunks = (SaveFileChunkPacket.MaxSaveBytes + SaveFileChunkPacket.MaxChunkBytes - 1)
			                / SaveFileChunkPacket.MaxChunkBytes;
			if (!Throws<ArgumentOutOfRangeException>(() =>
				SaveFileTransferManager.StartTransfer(1, "too-many", maxChunks + 1)))
				return UnitTestResult.Fail("Save transfer tracking accepted an oversized chunk count");
			if (!Throws<ArgumentException>(() => SaveFileTransferManager.StartTransfer(0, "valid", 1))
			    || !Throws<ArgumentException>(() => SaveFileTransferManager.StartTransfer(1, string.Empty, 1)))
				return UnitTestResult.Fail("Save transfer tracking accepted an invalid identity");

			return UnitTestResult.Pass("UDP save chunks and ACK tracking allocations are bounded");
		}

		[UnitTest(name: "Building snapshot: sender rejects invalid wire state", category: "Networking")]
		public static UnitTestResult BuildingSenderBounds()
		{
			if (!RejectsSerialize(new BuildingStatePacket { Buildings = null }, "count"))
				return UnitTestResult.Fail("BuildingStatePacket accepted a null collection");
			var tooMany = new List<BuildingState>(BuildingStatePacket.MaxBuildingCount + 1);
			for (int i = 0; i <= BuildingStatePacket.MaxBuildingCount; i++)
				tooMany.Add(default);
			if (!RejectsSerialize(new BuildingStatePacket { Buildings = tooMany }, "count"))
				return UnitTestResult.Fail("BuildingStatePacket accepted too many buildings");
			if (!RejectsSerialize(new BuildingStatePacket
			{
				Buildings = [new BuildingState { Cell = Grid.InvalidCell, PrefabName = "WireRefinedBridge" }]
			}, "cell")) return UnitTestResult.Fail("BuildingStatePacket accepted an invalid cell");
			if (!RejectsSerialize(new BuildingStatePacket
			{
				Buildings = [new BuildingState { Cell = 0, PrefabName = string.Empty }]
			}, "prefab")) return UnitTestResult.Fail("BuildingStatePacket accepted an empty prefab name");
			if (!RejectsSerialize(new BuildingStatePacket
			{
				Buildings = [new BuildingState { Cell = 0, PrefabName = new string('x', 257) }]
			}, "prefab")) return UnitTestResult.Fail("BuildingStatePacket accepted an oversized prefab name");

			return UnitTestResult.Pass("Building snapshots validate sender state before writing");
		}

		[UnitTest(name: "Building snapshot: total wire bytes are bounded before writing", category: "Networking")]
		public static UnitTestResult BuildingTotalWireBounds()
		{
			if (!TryGetValidCell(out int cell))
				return UnitTestResult.Skip("Building wire limit test requires an initialized world grid");
			const int entryBytes = sizeof(int) + 2 + BuildingStatePacket.MaxPrefabNameLength;
			int overflowCount = (BuildingStatePacket.MaxSerializedBodyBytes - sizeof(int)) / entryBytes + 1;
			var buildings = RepeatedBuildings(overflowCount, cell);
			using var stream = new MemoryStream();
			try
			{
				using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
				new BuildingStatePacket { Buildings = buildings }.Serialize(writer);
				return UnitTestResult.Fail("BuildingStatePacket accepted an oversized wire body");
			}
			catch (InvalidDataException)
			{
				return stream.Length == 0
					? UnitTestResult.Pass("Oversized building state is rejected before collection output")
					: UnitTestResult.Fail("BuildingStatePacket wrote collection bytes before rejecting it");
			}
		}

		[UnitTest(name: "Building snapshot: maximum legal wire body roundtrips", category: "Networking")]
		public static UnitTestResult BuildingMaximumWireRoundTrip()
		{
			if (!TryGetValidCell(out int cell))
				return UnitTestResult.Skip("Building boundary roundtrip requires an initialized world grid");
			const int entryBytes = sizeof(int) + 2 + BuildingStatePacket.MaxPrefabNameLength;
			int count = (BuildingStatePacket.MaxSerializedBodyBytes - sizeof(int)) / entryBytes;
			var packet = new BuildingStatePacket { Buildings = RepeatedBuildings(count, cell) };
			if (!PacketRegistry.HasRegisteredPacket(typeof(BuildingStatePacket)))
				PacketRegistry.TryRegister(typeof(BuildingStatePacket));
			byte[] wire = PacketSender.SerializePacketForSending(packet);
			if (wire.Length + sizeof(int) * 2 > ReliablePageChannel.MaxQueuedBytes)
				return UnitTestResult.Fail("Maximum legal building body exceeds page-channel admission");
			var copy = RoundTrip(packet);
			return copy.Buildings.Count == count
				? UnitTestResult.Pass("Maximum legal building wire body roundtrips")
				: UnitTestResult.Fail("Maximum legal building wire body changed during roundtrip");
		}

		[UnitTest(name: "Structure snapshot: sender identity and value are bounded", category: "Networking")]
		public static UnitTestResult StructureSenderIdentityBounds()
		{
			var invalidNetId = ValidStructurePacket();
			invalidNetId.NetId = 0;
			if (!RejectsSerialize(invalidNetId, "NetId"))
				return UnitTestResult.Fail("StructureStatePacket accepted an invalid NetId");

			var invalidCell = ValidStructurePacket();
			invalidCell.Cell = Grid.InvalidCell;
			if (!RejectsSerialize(invalidCell, "cell"))
				return UnitTestResult.Fail("StructureStatePacket accepted an invalid cell");

			var invalidVariant = ValidStructurePacket();
			invalidVariant.Value = new Variant { Type = (Variant.TypeCode)255 };
			if (!RejectsSerialize(invalidVariant, "variant type"))
				return UnitTestResult.Fail("StructureStatePacket accepted an invalid variant type");
			return UnitTestResult.Pass("Structure identity and primary values match receiver bounds");
		}

		[UnitTest(name: "Structure snapshot: sender optional values are bounded", category: "Networking")]
		public static UnitTestResult StructureSenderOptionalBounds()
		{
			var nullOptionals = ValidStructurePacket();
			nullOptionals.OptionalValues = null;
			if (!RejectsSerialize(nullOptionals, "count"))
				return UnitTestResult.Fail("StructureStatePacket accepted null optional values");

			var tooMany = ValidStructurePacket();
			for (int i = 0; i <= 256; i++)
				tooMany.OptionalValues[i.ToString()] = (Variant)i;
			if (!RejectsSerialize(tooMany, "count"))
				return UnitTestResult.Fail("StructureStatePacket accepted too many optional values");

			var invalidKey = ValidStructurePacket();
			invalidKey.OptionalValues[string.Empty] = (Variant)1;
			if (!RejectsSerialize(invalidKey, "key"))
				return UnitTestResult.Fail("StructureStatePacket accepted an empty optional key");

			var oversizedString = ValidStructurePacket();
			oversizedString.OptionalValues["string"] = (Variant)new string('x', 4097);
			if (!RejectsSerialize(oversizedString, "string"))
				return UnitTestResult.Fail("StructureStatePacket accepted an oversized string");

			var oversizedBytes = ValidStructurePacket();
			oversizedBytes.OptionalValues["bytes"] = (Variant)new byte[1024 * 1024 + 1];
			if (!RejectsSerialize(oversizedBytes, "byte array"))
				return UnitTestResult.Fail("StructureStatePacket accepted an oversized byte array");

			var oversizedBlob = ValidStructurePacket();
			oversizedBlob.OptionalValues["a"] = (Variant)new byte[600 * 1024];
			oversizedBlob.OptionalValues["b"] = (Variant)new byte[600 * 1024];
			if (!RejectsSerialize(oversizedBlob, "exceed"))
				return UnitTestResult.Fail("StructureStatePacket allocated an oversized optional blob");

			return UnitTestResult.Pass("Structure snapshots reject sender values outside receiver bounds");
		}

		[UnitTest(name: "Snapshot packets: legal boundary values roundtrip", category: "Networking")]
		public static UnitTestResult SnapshotBoundaryRoundTrips()
		{
			var save = RoundTrip(new SaveFileChunkPacket
			{
				FileName = "colony.sav", SnapshotGeneration = 1, Offset = 0,
				TotalSize = 16 * 1024, ChunkSize = 16 * 1024,
				FileHash = new byte[32], Chunk = new byte[16 * 1024]
			});
			if (save.Chunk.Length != 16 * 1024 || save.ChunkSize != 16 * 1024)
				return UnitTestResult.Fail("Legal UDP save chunk did not roundtrip");
			var tail = RoundTrip(new SaveFileChunkPacket
			{
				FileName = "colony.sav", SnapshotGeneration = 1, Offset = 16 * 1024,
				TotalSize = 16 * 1024 + 123, ChunkSize = 16 * 1024,
				FileHash = new byte[32], Chunk = new byte[123]
			});
			if (tail.Offset != 16 * 1024 || tail.Chunk.Length != 123)
				return UnitTestResult.Fail("Legal UDP save tail did not roundtrip");

			if (!TryGetValidCell(out int cell))
				return UnitTestResult.Skip("Structure roundtrip requires an initialized world grid");
			var building = RoundTrip(new BuildingStatePacket
			{
				Buildings = [new BuildingState { Cell = cell, PrefabName = "WireRefinedBridge" }]
			});
			if (building.Buildings.Count != 1 || building.Buildings[0].Cell != cell)
				return UnitTestResult.Fail("Legal building state did not roundtrip");

			var structure = ValidStructurePacket(cell);
			structure.OptionalValues["text"] = (Variant)"value";
			structure.OptionalValues["bytes"] = (Variant)new byte[] { 1, 2, 3 };
			var structureCopy = RoundTrip(structure);
			if (structureCopy.OptionalValues["text"].String != "value"
			    || structureCopy.OptionalValues["bytes"].ByteArray.Length != 3)
				return UnitTestResult.Fail("Legal structure optional values did not roundtrip");

			return UnitTestResult.Pass("Legal snapshot boundary values roundtrip unchanged");
		}

		private static StructureStatePacket ValidStructurePacket(int cell = 0)
			=> new StructureStatePacket
			{
				NetId = 1,
				Cell = cell,
				Revision = 1,
				SyncerTypeName = nameof(StructureStatePacket),
				Value = (Variant)42,
				OptionalValues = new Dictionary<string, Variant>()
			};

		private static bool RejectsSaveMetadata(int chunkSize)
		{
			try
			{
				SaveFileChunkPacket.ValidateMetadata(0, chunkSize, chunkSize, chunkSize);
				return false;
			}
			catch (InvalidDataException)
			{
				return true;
			}
		}

		private static bool RejectsSaveMetadata(int offset, int totalSize, int length)
		{
			try
			{
				SaveFileChunkPacket.ValidateMetadata(
					offset, totalSize, SaveFileChunkPacket.MaxChunkBytes, length);
				return false;
			}
			catch (InvalidDataException)
			{
				return true;
			}
		}

		private static List<BuildingState> RepeatedBuildings(int count, int cell)
		{
			string prefab = new string('x', BuildingStatePacket.MaxPrefabNameLength);
			var buildings = new List<BuildingState>(count);
			for (int i = 0; i < count; i++)
				buildings.Add(new BuildingState { Cell = cell, PrefabName = prefab });
			return buildings;
		}

		private static bool RejectsSerialize(IPacket packet, string expectedMessage)
		{
			try
			{
				using var stream = new MemoryStream();
				using var writer = new BinaryWriter(stream);
				packet.Serialize(writer);
				return false;
			}
			catch (InvalidDataException ex)
			{
				return ex.Message.IndexOf(expectedMessage, StringComparison.OrdinalIgnoreCase) >= 0;
			}
		}

		internal static bool TryGetValidCell(out int cell)
		{
			for (cell = 0; cell < Grid.CellCount; cell++)
				if (Grid.IsValidCell(cell))
					return true;
			cell = Grid.InvalidCell;
			return false;
		}

		private static bool Throws<T>(System.Action action) where T : Exception
		{
			try
			{
				action();
				return false;
			}
			catch (T)
			{
				return true;
			}
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
