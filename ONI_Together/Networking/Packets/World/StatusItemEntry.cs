using Shared.Profiling;
using System.IO;

namespace ONI_Together.Networking.Packets.World
{
    public struct StatusItemEntry
    {
        public string ItemId;
        public string CategoryId;
        public string DisplayName;
        public string Tooltip;

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();
            writer.Write(ItemId ?? string.Empty);
            writer.Write(CategoryId ?? string.Empty);
            writer.Write(DisplayName ?? string.Empty);
            writer.Write(Tooltip ?? string.Empty);
        }

        public static StatusItemEntry Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();
            return new StatusItemEntry
            {
                ItemId = reader.ReadString(),
                CategoryId = reader.ReadString(),
                DisplayName = reader.ReadString(),
                Tooltip = reader.ReadString(),
            };
        }
    }
}
