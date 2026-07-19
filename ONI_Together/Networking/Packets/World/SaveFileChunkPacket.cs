using ONI_Together.Misc;
using ONI_Together.DebugTools;
using ONI_Together.Misc.World;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.World
{
	public class SaveFileChunkPacket : IPacket, IHostOnlyPacket
	{
		public const int MaxSaveBytes = 1024 * 1024 * 1024;
		public const int MaxChunkBytes = 16 * 1024;
		public const int MaxFileNameChars = 512;
		public string FileName;
		public long SnapshotGeneration;
		public int Offset;
		public int TotalSize;
		public int ChunkSize;
		public byte[] FileHash;
		public byte[] Chunk;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			if (string.IsNullOrEmpty(FileName) || FileName.Length > MaxFileNameChars
			    || SnapshotGeneration <= 0
			    || FileHash == null || FileHash.Length != 32 || Chunk == null)
				throw new InvalidDataException("Invalid save chunk metadata");
			ValidateMetadata(Offset, TotalSize, ChunkSize, Chunk.Length);

			writer.Write(FileName);
			writer.Write(SnapshotGeneration);
			writer.Write(Offset);
			writer.Write(TotalSize);
			writer.Write(ChunkSize);
			writer.Write(FileHash.Length);
			writer.Write(FileHash);
			writer.Write(Chunk.Length);
			writer.Write(Chunk);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			FileName = reader.ReadString();
			if (FileName.Length == 0 || FileName.Length > MaxFileNameChars)
				throw new InvalidDataException("Invalid save filename length");
			SnapshotGeneration = reader.ReadInt64();
			if (SnapshotGeneration <= 0)
				throw new InvalidDataException("Invalid save snapshot generation");
			Offset = reader.ReadInt32();
			TotalSize = reader.ReadInt32();
			ChunkSize = reader.ReadInt32();
			int hashLength = reader.ReadInt32();
			if (hashLength != 32)
				throw new InvalidDataException("Invalid save hash length");
			FileHash = reader.ReadBytes(hashLength);
			if (FileHash.Length != hashLength)
				throw new EndOfStreamException("Save hash is truncated");
			int length = reader.ReadInt32();
			ValidateMetadata(Offset, TotalSize, ChunkSize, length);
			Chunk = reader.ReadBytes(length);
			if (Chunk.Length != length)
				throw new EndOfStreamException("Save chunk is truncated");
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			DebugConsole.LogWarning("[SaveFileChunkPacket] Rejected unwrapped save chunk");
		}

		internal static void ValidateMetadata(int offset, int totalSize, int chunkSize, int length)
		{
			if (totalSize <= 0 || totalSize > MaxSaveBytes)
				throw new InvalidDataException($"Invalid save size: {totalSize}");
			if (chunkSize != MaxChunkBytes)
				throw new InvalidDataException($"Invalid save chunk size: {length}/{chunkSize}");
			if (offset < 0 || offset >= totalSize || offset % chunkSize != 0)
				throw new InvalidDataException($"Invalid save chunk offset: {offset}");
			int expectedLength = System.Math.Min(chunkSize, totalSize - offset);
			if (length != expectedLength)
				throw new InvalidDataException($"Invalid save chunk length: {length}/{expectedLength}");
		}
	}
}
