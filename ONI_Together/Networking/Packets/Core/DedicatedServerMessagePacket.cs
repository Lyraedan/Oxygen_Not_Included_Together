using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Core
{
    public class DedicatedServerMessagePacket : IPacket
    {
        public int PacketID;
        public byte[] PacketData;
        public int SendType; // Reliable, Unreliable

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();

            writer.Write(PacketID);
            writer.Write(SendType);
            writer.Write(PacketData.Length);
            writer.Write(PacketData);
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();

            PacketID = reader.ReadInt32();
            SendType = reader.ReadInt32();
            int length = reader.ReadInt32();
            PacketData = reader.ReadBytes(length);
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (!PacketRegistry.HasRegisteredPacket(PacketID))
            {
                DebugConsole.LogWarning("Received a non-registered packet from the dedicated server");
                return;
            }

            DebugConsole.Log("Recieved a packet from a dedicated server with packet id: " + PacketID);

            var packet = PacketRegistry.Create(PacketID);

            using (var ms = new MemoryStream(PacketData))
            using (var reader = new BinaryReader(ms))
            {
                packet.Deserialize(reader);
            }

            packet.OnDispatched();
        }
    }
}
