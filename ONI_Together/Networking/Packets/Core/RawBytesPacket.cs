using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ONI_Together.Networking.Packets.Architecture;

namespace ONI_Together.Networking.Packets.Core
{
    public class RawBytesPacket : IPacket
    {
        public byte[] Data;
        public RawBytesPacket(byte[] data) => Data = data;
        public void Serialize(BinaryWriter writer) => writer.Write(Data);
        public void Deserialize(BinaryReader reader) => Data = reader.ReadBytes(reader.ReadInt32());
        public void OnDispatched() { }
    }
}
