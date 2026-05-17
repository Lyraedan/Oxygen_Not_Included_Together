using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
    public class StatusItemGroupPacket : IPacket, IBulkablePacket
    {
        public enum ItemGroupPacketAction
        {
            Add,
            Remove
        }

        public int MaxPackSize => 500;

        public uint IntervalMs => 50;

        public int NetId;
        public ItemGroupPacketAction Action;

        public string StatusItemId;
        public bool Immediate;

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();

            writer.Write(NetId);
            writer.Write((int)Action);
            writer.Write(StatusItemId);
            switch (Action)
            {
                case ItemGroupPacketAction.Remove:
                    writer.Write(Immediate);
                    break;
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();

            NetId = reader.ReadInt32();
            Action = (ItemGroupPacketAction)reader.ReadInt32();
            StatusItemId = reader.ReadString() ?? string.Empty;
            switch(Action)
            {
                case ItemGroupPacketAction.Remove:
                    Immediate = reader.ReadBoolean();
                    break;
            }
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (!NetworkIdentityRegistry.TryGet(NetId, out NetworkIdentity identity))
            {
                DebugConsole.LogWarning($"[StatusItemGroupPacket] No network identity for {NetId}");
                return;
            }

            switch(Action)
            {
                case ItemGroupPacketAction.Add:
                    AddStatusItemGroup(identity);
                    break;
                case ItemGroupPacketAction.Remove:
                    RemoveStatusItemGroup(identity);
                    break;
            }
        }

        public void AddStatusItemGroup(NetworkIdentity identity)
        {

        }

        public void RemoveStatusItemGroup(NetworkIdentity identity)
        {

        }
    }
}
