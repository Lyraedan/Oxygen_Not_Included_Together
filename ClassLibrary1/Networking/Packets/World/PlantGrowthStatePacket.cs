using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;
using ONI_MP.Networking;

namespace ONI_MP.Networking.Packets.World
{
	public struct PlantData
	{
		public int PlantNetId;
		public int ReceptacleNetId;
		public int Cell;
		public string PlantPrefabTag;
		public float Maturity;
		public bool IsWilting;
		public bool IsHarvestReady;
		public bool IsWild;
	}

	public class PlantGrowthStatePacket : IPacket
	{
		public List<PlantData> Plants = new List<PlantData>();

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(Plants.Count);
			foreach (var p in Plants)
			{
				writer.Write(p.PlantNetId);
				writer.Write(p.ReceptacleNetId);
				writer.Write(p.Cell);
				writer.Write(p.PlantPrefabTag ?? string.Empty);
				writer.Write(p.Maturity);
				writer.Write(p.IsWilting);
				writer.Write(p.IsHarvestReady);
				writer.Write(p.IsWild);
			}
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			int count = reader.ReadInt32();
			Plants = new List<PlantData>(count);

			for (int i = 0; i < count; i++)
			{
				Plants.Add(new PlantData
				{
					PlantNetId = reader.ReadInt32(),
					ReceptacleNetId = reader.ReadInt32(),
					Cell = reader.ReadInt32(),
					PlantPrefabTag = reader.ReadString(),
					Maturity = reader.ReadSingle(),
					IsWilting = reader.ReadBoolean(),
					IsHarvestReady = reader.ReadBoolean(),
					IsWild = reader.ReadBoolean()
				});
			}
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost) return;

			PlantGrowthSyncer.Instance?.OnPlantStateReceived(this);
		}
	}
}
