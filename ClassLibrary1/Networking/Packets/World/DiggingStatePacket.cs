using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;
using ONI_MP.Networking;

namespace ONI_MP.Networking.Packets.World
{
	public class DiggingStatePacket : IPacket
	{
		public List<int> DigCells = new List<int>();

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(DigCells.Count);
			foreach (var cell in DigCells)
			{
				writer.Write(cell);
			}
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			int count = reader.ReadInt32();
			DigCells = new List<int>(count);
			for (int i = 0; i < count; i++)
			{
				DigCells.Add(reader.ReadInt32());
			}
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost) return;

			WorldStateSyncer.Instance?.OnDiggingStateReceived(this);
		}
	}
}
