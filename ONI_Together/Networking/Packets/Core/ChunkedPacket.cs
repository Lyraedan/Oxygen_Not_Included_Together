using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;

namespace ONI_Together.Networking.Packets.Core
{
	internal class ChunkedPacket : IPacket
	{
		public int SequenceId;
		public int ChunkIndex;
		public int TotalChunks;
		public byte[] ChunkData;

		private static Dictionary<int, byte[][]> _pendingChunks = new Dictionary<int, byte[][]>();
		private static int _nextSequenceId = 0;

		public ChunkedPacket() { }

		public void Serialize(BinaryWriter writer)
		{
			writer.Write(SequenceId);
			writer.Write(ChunkIndex);
			writer.Write(TotalChunks);
			writer.Write(ChunkData.Length);
			writer.Write(ChunkData);
		}

		public void Deserialize(BinaryReader reader)
		{
			SequenceId = reader.ReadInt32();
			ChunkIndex = reader.ReadInt32();
			TotalChunks = reader.ReadInt32();
			int len = reader.ReadInt32();
			ChunkData = reader.ReadBytes(len);
		}

		public void OnDispatched()
		{
			if (!_pendingChunks.TryGetValue(SequenceId, out var chunks))
			{
				chunks = new byte[TotalChunks][];
				_pendingChunks[SequenceId] = chunks;
			}

			chunks[ChunkIndex] = ChunkData;

			for (int i = 0; i < TotalChunks; i++)
			{
				if (chunks[i] == null)
					return;
			}

			_pendingChunks.Remove(SequenceId);

			int totalSize = 0;
			foreach (var chunk in chunks)
			{
				totalSize += chunk.Length;
			}

			byte[] fullData = new byte[totalSize];
			int offset = 0;
			foreach (var chunk in chunks)
			{
				System.Array.Copy(chunk, 0, fullData, offset, chunk.Length);
				offset += chunk.Length;
			}

			PacketHandler.HandleIncoming(fullData);
		}

		public static int GetNextSequenceId()
		{
			return _nextSequenceId++;
		}
	}
}
