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
        public ulong SenderId;
        public int PacketID;
        public byte[] PacketData;
        public int SendType;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(PacketID);
            writer.Write(SendType);
            writer.Write(SenderId);
            writer.Write(PacketData.Length);
            writer.Write(PacketData);
        }

        public void Deserialize(BinaryReader reader)
        {
            PacketID = reader.ReadInt32();
            SendType = reader.ReadInt32();
            SenderId = reader.ReadUInt64();
            int length = reader.ReadInt32();
            PacketData = reader.ReadBytes(length);
        }

        public void OnDispatched()
        {
            if (!MultiplayerSession.IsHost && MultiplayerSession.LocalUserID == MultiplayerSession.HostUserID)
            {
                MultiplayerSession.IsHost = true;
                MultiplayerSession.IsBehindDedicatedServer = true;
            }

            if (SenderId != 0 && MultiplayerSession.IsHost && !MultiplayerSession.ConnectedPlayers.ContainsKey(SenderId))
            {
                var player = new MultiplayerPlayer(SenderId);
                if (MultiplayerSession.ConnectedPlayers.TryGetValue(MultiplayerSession.HostUserID, out var hostPlayer))
                {
                    player.Connection = hostPlayer.Connection;
                    MultiplayerSession.ConnectedPlayers[SenderId] = player;
                }
            }

            MultiplayerSession.IsBehindDedicatedServer = true;

            if (!PacketRegistry.HasRegisteredPacket(PacketID))
            {
                DebugConsole.LogWarning("Received a non-registered packet from the dedicated server");
                return;
            }

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
