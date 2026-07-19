using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.Core
{
    public class DedicatedServerMessagePacket : IPacket, IHostOnlyPacket
    {
        public int PacketID;
        public byte[] PacketData;
        public int SendType; // Reliable, Unreliable
        public ulong SenderId;
        public bool SenderIsHost;

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();

			Validate();
			writer.Write(PacketID);
            writer.Write(SendType);
            writer.Write(SenderId);
            writer.Write(SenderIsHost);
            writer.Write(PacketData.Length);
            writer.Write(PacketData);
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();

            PacketID = reader.ReadInt32();
            SendType = reader.ReadInt32();
            SenderId = reader.ReadUInt64();
            SenderIsHost = reader.ReadBoolean();
            int length = reader.ReadInt32();
            if (length < sizeof(int) || length > PacketHandler.MaxPacketSize)
                throw new InvalidDataException($"Invalid dedicated relay payload length: {length}");
            if (reader.BaseStream.CanSeek && reader.BaseStream.Length - reader.BaseStream.Position < length)
                throw new EndOfStreamException("Dedicated relay payload is truncated");
            PacketData = reader.ReadBytes(length);
            if (PacketData.Length != length)
                throw new EndOfStreamException("Dedicated relay payload is truncated");
			Validate();
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

			Validate();
			DebugConsole.Log("Recieved a packet from a dedicated server with packet id: " + PacketID);
			if (!PacketHandler.TryHandleIncoming(
				    PacketData,
				    PacketHandler.CurrentContext))
				throw new InvalidDataException("Dedicated relay payload was rejected");
        }

		private void Validate()
		{
			if (PacketData == null || PacketData.Length < sizeof(int)
			    || PacketData.Length > PacketHandler.MaxPacketSize
			    || BitConverter.ToInt32(PacketData, 0) != PacketID
			    || ReliablePageChannel.IsForbiddenDedicatedFrame(PacketData))
				throw new InvalidDataException("Invalid dedicated relay payload");
		}
    }
}
