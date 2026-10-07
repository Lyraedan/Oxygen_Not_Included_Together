using System.Collections.Generic;
using System.IO;
using ONI_Together.Misc;

namespace ONI_Together.Networking.Packets.World
{
    internal static class StatePacketValues
    {
        public static Dictionary<string, Variant> Read(BinaryReader reader)
        {
            int size = reader.ReadInt32();
            if (size < 4 || size > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Invalid building state length");
            using var values = new BinaryReader(new MemoryStream(reader.ReadBytes(size)));
            int count = values.ReadInt32();
            if (count < 0 || count > (size - 4) / 3)
                throw new InvalidDataException("Invalid building state count");
            var result = new Dictionary<string, Variant>(count);
            for (int i = 0; i < count; i++)
                if (!result.TryAdd(values.ReadString(), Variant.Read(values)))
                    throw new InvalidDataException("Duplicate building state field");
            if (values.BaseStream.Position != size)
                throw new InvalidDataException("Trailing building state data");
            return result;
        }
    }
}
