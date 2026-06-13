using Shared.Profiling;
using System.IO;

namespace ONI_Together.Networking.Packets.World
{
    public struct StatusItemEntry
    {
        public int ItemHash;
        public int CategoryHash;
        public string DisplayName;
        public string Tooltip;

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();
            writer.Write(ItemHash);
            writer.Write(CategoryHash);

            byte flags = 0;
            if (DisplayName != null) flags |= 1;
            if (Tooltip != null) flags |= 2;
            writer.Write(flags);

            if ((flags & 1) != 0) writer.Write(DisplayName);
            if ((flags & 2) != 0) writer.Write(Tooltip);
        }

        public static StatusItemEntry Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();
            int itemHash = reader.ReadInt32();
            int categoryHash = reader.ReadInt32();
            byte flags = reader.ReadByte();

            return new StatusItemEntry
            {
                ItemHash = itemHash,
                CategoryHash = categoryHash,
                DisplayName = (flags & 1) != 0 ? reader.ReadString() : null,
                Tooltip = (flags & 2) != 0 ? reader.ReadString() : null,
            };
        }
    }
}
