#if DEBUG
using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;

namespace ONI_Together.Tests
{
    public class TestPacket : IPacket
    {
        public ulong ClientID;

        public static bool WasDispatched { get; private set; }
        public static ulong PayloadClientId { get; private set; }
        public static ulong SenderId { get; private set; }
        public static bool SenderIsHost { get; private set; }

        public static void Reset()
        {
            WasDispatched = false;
            PayloadClientId = 0;
            SenderId = 0;
            SenderIsHost = false;
        }

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

            PayloadClientId = ClientID;
            SenderId = PacketHandler.CurrentContext.SenderId;
            SenderIsHost = PacketHandler.CurrentContext.SenderIsHost;
            WasDispatched = true;
            DebugConsole.Log($"[TestPacket] Received test packet from transport sender {SenderId} (payload ClientID: {ClientID})");
        }
    }
}
#endif
