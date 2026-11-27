using System;

namespace ONI_Together_DedicatedServer
{
    public static class Utils
    {
        public static string FormatBytes(long bytes)
        {
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
            using var ms = new System.IO.MemoryStream();
            using var writer = new System.IO.BinaryWriter(ms);
            writer.Write(packetType);
            serialize(writer);
            return ms.ToArray();
        }
    }
}
