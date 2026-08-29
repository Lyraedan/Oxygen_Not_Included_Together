using System;
using Riptide;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;

namespace ONI_Together.Networking.Transport.Lan
{
    public class RiptidePacketSender : TransportPacketSender
    {
        public override bool SendPacket(object conn, IPacket packet, PacketSendMode sendType = PacketSendMode.ReliableImmediate)
        {
            using var _ = Profiler.Scope();

            if (conn is not Connection connection)
                return false;

            if (!connection.IsConnected)
                return false;

            byte[] bytes = PacketSender.SerializePacketForSending(packet);

            return SendChunkedIfNeeded(connection, bytes, packet, sendType, (c, b, p, s) => SendRaw((Connection)c, b, p, s));
        }

        private bool SendRaw(Connection connection, byte[] bytes, IPacket packet, PacketSendMode sendType)
        {
            MessageSendMode sendMode = ConvertSendType(sendType);
            int id = PacketRegistry.GetPacketId(packet);
            Riptide.Message msg = Riptide.Message.Create(sendMode, 1);
            msg.AddBytes(bytes);

            if (MultiplayerSession.IsHost)
            {
                var server = RiptideServer.ServerInstance;
                if (server == null)
                    return false;

                server.Send(msg, connection);
            }
            else
            {
                var client = RiptideClient.Client;
                if (client == null)
                    return false;

                client.Send(msg);
            }

            PacketTracker.TrackSent(new PacketTracker.PacketTrackData
            {
                packet = packet,
                size = bytes.Length
            });
            return true;
        }

        private static MessageSendMode ConvertSendType(PacketSendMode sendType)
        {
            using var _ = Profiler.Scope();

            switch (sendType)
            {
                case PacketSendMode.Reliable:
                case PacketSendMode.ReliableImmediate:
                    return MessageSendMode.Reliable;

                case PacketSendMode.Unreliable:
                case PacketSendMode.UnreliableImmediate:
                case PacketSendMode.UnreliableNoDelay:
                    return MessageSendMode.Unreliable;

                default:
                    // Catch-all for unexpected flag combinations
                    if ((sendType & PacketSendMode.Reliable) != 0)
                        return MessageSendMode.Reliable;

                    return MessageSendMode.Unreliable;
            }
        }
    }
}
