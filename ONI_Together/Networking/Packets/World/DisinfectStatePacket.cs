using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	public class DisinfectStatePacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		internal const int MaxCellCount = 262144;
		public List<int> DisinfectCells = new List<int>();

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(DisinfectCells.Count);
			foreach (var cell in DisinfectCells)
			{
				writer.Write(cell);
			}
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			int count = reader.ReadInt32();
			if (count < 0 || count > MaxCellCount)
				throw new InvalidDataException($"Invalid disinfect cell count: {count}");
			DisinfectCells = new List<int>(count);
			for (int i = 0; i < count; i++)
			{
				DisinfectCells.Add(reader.ReadInt32());
			}
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost) return;
			ONI_Together.Networking.Components.WorldStateSyncer.Instance?.OnDisinfectStateReceived(this);
		}
	}
}
