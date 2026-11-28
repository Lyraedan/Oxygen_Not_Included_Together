using ONI_Together.DebugTools;
using ONI_Together.Networking.Overlay;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System;

namespace ONI_Together.Networking.Transport.External
{
    /// <summary>
    /// Serializes packets and hands raw bytes to a foreign transport.
    /// Fragmentation, bulk batching and the optional send queue stay in the main mod.
    /// </summary>
    public sealed class ExternalTransportPacketSender : TransportPacketSender
    {
        private readonly ExternalTransportServer _server;
        private readonly ExternalTransportClient _client;

        public ExternalTransportPacketSender(ExternalTransportServer server, ExternalTransportClient client)
        {
            _server = server;
            _client = client;
        }

        public override bool SendPacket(object conn, IPacket packet, PacketSendMode sendType = PacketSendMode.ReliableImmediate)
        {
            using var _ = Profiler.Scope();

            if (conn is not ulong playerId)
                return false;

            if (packet is IPriorityPacket)
                sendType |= PacketSendMode.Priority;

            byte[] bytes = PacketSender.SerializePacketForSending(packet);
            return SendChunkedIfNeeded(conn, bytes, packet, sendType,
                (_, chunkBytes, chunkPacket, chunkMode) => SendRaw(playerId, chunkBytes, chunkPacket, chunkMode));
        }

        private bool SendRaw(ulong playerId, byte[] bytes, IPacket packet, PacketSendMode sendMode)
        {
            bool sent;

            if (MultiplayerSession.IsHost)
            {
                if (_server == null)
                    return false;
                sent = _server.SendRaw(playerId, bytes, sendMode);
            }
            else
            {
                if (_client == null)
                    return false;
                sent = _client.SendRaw(playerId, bytes, sendMode);
            }

            if (!sent)
                return false;

            PacketTracker.TrackSent(new PacketTracker.PacketTrackData
            {
                packet = packet,
                size = bytes.Length
            });

            NetIdActivityTracker.RecordOutgoing(packet, bytes.Length);
            return true;
        }
    }
}
