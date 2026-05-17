#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;

namespace ONI_Together.Tests
{
    public class TestPacket : IPacket
    {

        public ulong ClientID;

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();

            writer.Write(ClientID);
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();

            ClientID = reader.ReadUInt64();
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            DebugConsole.Log($"[TestPacket] Recieved test packet from: {ClientID}");
        }
    }
}
#endif