using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System;
using System.IO;
using System.Text;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
    public struct BuildingState
    {
        public int Cell;
        public string PrefabName;
    }

    public class BuildingStatePacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
    {
        internal const int MaxBuildingCount = 262144;
        internal const int MaxPrefabNameLength = 256;
		internal const int MaxSerializedBodyBytes =
			global::ONI_Together.Networking.ReliablePageChannel.MaxQueuedBytes - sizeof(int) * 3;
		private static readonly Encoding WireEncoding = new UTF8Encoding(false, true);
        public List<BuildingState> Buildings = new List<BuildingState>();

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();
			ValidateForSerialize();

            writer.Write(Buildings.Count);
            foreach (var b in Buildings)
            {
                writer.Write(b.Cell);
				writer.Write(b.PrefabName);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();
			long bodyStart = reader.BaseStream.Position;

            int count = reader.ReadInt32();
            if (count < 0 || count > MaxBuildingCount)
                throw new InvalidDataException($"Invalid building state count: {count}");
            Buildings = new List<BuildingState>(count);

            for (int i = 0; i < count; i++)
            {
                int cell = reader.ReadInt32();
                string prefabName = reader.ReadString();
				ValidateBuilding(cell, prefabName);
				if (reader.BaseStream.Position - bodyStart > MaxSerializedBodyBytes)
					throw new InvalidDataException("Building state exceeds the wire size limit");
                Buildings.Add(new BuildingState
                {
                    Cell = cell,
                    PrefabName = prefabName
                });
            }
        }

		private void ValidateForSerialize()
		{
			if (Buildings == null || Buildings.Count > MaxBuildingCount)
				throw new InvalidDataException($"Invalid building state count: {Buildings?.Count ?? -1}");
			long bodyBytes = sizeof(int);
			foreach (var building in Buildings)
			{
				ValidateBuilding(building.Cell, building.PrefabName);
				bodyBytes += sizeof(int) + GetStringWireBytes(building.PrefabName);
				if (bodyBytes > MaxSerializedBodyBytes)
					throw new InvalidDataException("Building state exceeds the wire size limit");
			}
		}

		private static void ValidateBuilding(int cell, string prefabName)
		{
			if (string.IsNullOrEmpty(prefabName) || prefabName.Length > MaxPrefabNameLength)
				throw new InvalidDataException("Invalid building prefab name");
			if (!Grid.IsValidCell(cell))
				throw new InvalidDataException($"Invalid building cell: {cell}");
		}

		private static int GetStringWireBytes(string value)
		{
			try
			{
				int byteCount = WireEncoding.GetByteCount(value);
				int prefixBytes = 1;
				for (int remaining = byteCount; (remaining >>= 7) != 0; prefixBytes++) { }
				return prefixBytes + byteCount;
			}
			catch (EncoderFallbackException ex)
			{
				throw new InvalidDataException("Building prefab name contains invalid UTF-16", ex);
			}
		}

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (MultiplayerSession.IsHost)
                return;

            Components.BuildingSyncer.Instance?.OnPacketReceived(this);
        }
    }
}
