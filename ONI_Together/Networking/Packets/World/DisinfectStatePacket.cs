using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	public class DisinfectStatePacket : IPacket
	{
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
