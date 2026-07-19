using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	public struct PlantData
	{
		internal const int MaxPrefabTagLength = 256;

		public int PlantNetId;
		public ulong LifecycleRevision;
		public int ReceptacleNetId;
		public int Cell;
		public string PlantPrefabTag;
		public float Maturity;
		public bool IsWilting;
		public bool IsHarvestReady;
		public bool IsWild;

		internal readonly void Serialize(BinaryWriter writer)
		{
			writer.Write(PlantNetId);
			writer.Write(LifecycleRevision);
			writer.Write(ReceptacleNetId);
			writer.Write(Cell);
			writer.Write(PlantPrefabTag ?? string.Empty);
			writer.Write(Maturity);
			writer.Write(IsWilting);
			writer.Write(IsHarvestReady);
			writer.Write(IsWild);
		}

		internal static PlantData Deserialize(BinaryReader reader)
		{
			var data = new PlantData
			{
				PlantNetId = reader.ReadInt32(),
				LifecycleRevision = reader.ReadUInt64(),
				ReceptacleNetId = reader.ReadInt32(),
				Cell = reader.ReadInt32(),
				PlantPrefabTag = reader.ReadString(),
				Maturity = reader.ReadSingle(),
				IsWilting = reader.ReadBoolean(),
				IsHarvestReady = reader.ReadBoolean(),
				IsWild = reader.ReadBoolean()
			};
			if (!IsValid(data))
				throw new InvalidDataException("Invalid plant lifecycle state");
			return data;
		}

		internal static bool IsValid(PlantData data)
		{
			return data.PlantNetId != 0 && data.LifecycleRevision != 0
			       && !string.IsNullOrEmpty(data.PlantPrefabTag)
			       && data.PlantPrefabTag.Length <= MaxPrefabTagLength
			       && !float.IsNaN(data.Maturity) && !float.IsInfinity(data.Maturity);
		}
	}

	public class PlantGrowthStatePacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		internal const int MaxPlantCount = 65536;
		public ulong SnapshotRevision;
		public List<PlantData> Plants = new List<PlantData>();

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(SnapshotRevision);
			writer.Write(Plants.Count);
			foreach (var p in Plants)
				p.Serialize(writer);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			SnapshotRevision = reader.ReadUInt64();
			if (SnapshotRevision == 0)
				throw new InvalidDataException("Invalid plant snapshot revision");
			int count = reader.ReadInt32();
			if (count < 0 || count > MaxPlantCount)
				throw new InvalidDataException($"Invalid plant state count: {count}");
			Plants = new List<PlantData>(count);
			var plantIds = new HashSet<int>();

			for (int i = 0; i < count; i++)
			{
				PlantData data = PlantData.Deserialize(reader);
				if (!plantIds.Add(data.PlantNetId))
					throw new InvalidDataException($"Duplicate plant NetId: {data.PlantNetId}");
				Plants.Add(data);
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
