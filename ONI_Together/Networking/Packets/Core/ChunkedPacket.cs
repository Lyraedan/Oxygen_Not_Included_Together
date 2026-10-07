using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;

namespace ONI_Together.Networking.Packets.Core
{
	internal class ChunkedPacket : IPacket, ISenderAwarePacket
	{
        public ulong? SenderId { get; set; }
		public int SequenceId;
		public int ChunkIndex;
		public int TotalChunks;
		public byte[] ChunkData;

        private static readonly Refresh.TransportChunkAssembler assembler = new();
        public static void ClearPending() => assembler.Clear();
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
            if (TotalChunks <= 0 || TotalChunks > 65536 || ChunkIndex < 0 || ChunkIndex >= TotalChunks
                || len < 0 || len > 65536 || len > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Invalid transport chunk");
			ChunkData = reader.ReadBytes(len);
		}

		public void OnDispatched()
		{
            byte[] fullData = assembler.Add(SenderId, SequenceId, ChunkIndex, TotalChunks, ChunkData, UnityEngine.Time.unscaledTime);
            if (fullData != null) PacketHandler.HandleIncoming(fullData, SenderId);
		}

		public static int GetNextSequenceId()
		{
			return _nextSequenceId++;
		}
	}
}
