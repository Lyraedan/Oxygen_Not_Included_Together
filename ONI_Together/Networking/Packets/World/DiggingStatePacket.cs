using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	public class DiggingStatePacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		internal const int MaxCellCount = 262144;
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
			if (count < 0 || count > MaxCellCount)
				throw new InvalidDataException($"Invalid digging cell count: {count}");
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
