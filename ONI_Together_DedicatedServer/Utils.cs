using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ONI_Together.Networking.Packets.Architecture;
using Shared;
using Shared.Profiling;

namespace ONI_Together_DedicatedServer
{
    public static class Utils
    {
        private const string DedicatedServerPacketTypeName =
            "ONI_Together.Networking.Packets.Core.DedicatedServerMessagePacket";

        public static readonly int DEDICATED_SERVER_PACKET_ID =
            NetworkingHash.ForString(DedicatedServerPacketTypeName);

        public static string FormatBytes(long bytes)
        {
            using var _ = Profiler.Scope();

            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        public static byte[] SerializePacketForSending(int packetType, Action<System.IO.BinaryWriter> serialize)
        {
            using var _ = Profiler.Scope();

            using (var ms = new System.IO.MemoryStream())
            using (var writer = new System.IO.BinaryWriter(ms))
            {
                writer.Write(packetType);
                serialize(writer);
                return ms.ToArray();
            }
        }
    }
}
