using ONI_Together.Misc;
using ONI_Together.Misc.World;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	public class SaveFileChunkPacket : IPacket
	{
		public string FileName;
		public int Offset;
		public int TotalSize;
		public byte[] Chunk;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(FileName);
			writer.Write(Offset);
			writer.Write(TotalSize);
			writer.Write(Chunk.Length);
			writer.Write(Chunk);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			FileName = reader.ReadString();
			Offset = reader.ReadInt32();
			TotalSize = reader.ReadInt32();
			int length = reader.ReadInt32();
			Chunk = reader.ReadBytes(length);
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (Utils.IsInGame() && !GameClient.IsHardSyncInProgress)
			{
				return;
			}

			SaveChunkAssembler.ReceiveChunk(this);
		}
	}
}
