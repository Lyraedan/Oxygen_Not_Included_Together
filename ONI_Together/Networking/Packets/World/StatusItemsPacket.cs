using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System;
using System.Collections.Generic;
using System.IO;

namespace ONI_Together.Networking.Packets.World
{
    public class StatusItemsPacket : IPacket
    {
        public const int MaxEntries = 64;

        public int DupeNetId;
        public List<StatusItemEntry> Entries = new();

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();
            writer.Write(DupeNetId);
            int count = Math.Min(Entries.Count, MaxEntries);
            writer.Write(count);
            for (int i = 0; i < count; i++)
                Entries[i].Serialize(writer);
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();
            DupeNetId = reader.ReadInt32();
            int count = reader.ReadInt32();
            if (count < 0 || count > MaxEntries)
            {
                Entries = new List<StatusItemEntry>();
                return;
            }
            Entries = new List<StatusItemEntry>(count);
            for (int i = 0; i < count; i++)
                Entries.Add(StatusItemEntry.Deserialize(reader));
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();
            if (!MultiplayerSession.IsClient)
                return;
            if (!NetworkIdentityRegistry.TryGet(DupeNetId, out var entity))
                return;
            if (!entity.TryGetComponent<ClientReceiver_StatusItems>(out var receiver))
                return;

            receiver.Apply(Entries);
        }
    }
}
