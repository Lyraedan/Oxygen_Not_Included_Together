using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
    public struct BuildingState
    {
        public int Cell;
        public string PrefabName;
    }

    public class BuildingStatePacket : IPacket
    {
        public List<BuildingState> Buildings = new List<BuildingState>();

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();

            writer.Write(Buildings.Count);
            foreach (var b in Buildings)
            {
                writer.Write(b.Cell);
                writer.Write(b.PrefabName ?? string.Empty);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();

            int count = reader.ReadInt32();
            Buildings = new List<BuildingState>(count);

            for (int i = 0; i < count; i++)
            {
                Buildings.Add(new BuildingState
                {
                    Cell = reader.ReadInt32(),
                    PrefabName = reader.ReadString()
                });
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