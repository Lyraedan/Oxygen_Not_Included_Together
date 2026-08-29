using LiteNetLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using Shared.Profiling;
using System;

namespace ONI_Together.Networking.Transport.Lan
{
    public class LiteNetLibPacketSender : TransportPacketSender
    {
        // Same payload budget RiptidePacketSender enforces, kept in step with it so a batch
        // sized for one LAN transport is not oversized on the other.
        private const int MAX_PAYLOAD_BYTES = 1000;

        public override bool SendPacket(object conn, IPacket packet, PacketSendMode sendType = PacketSendMode.ReliableImmediate)
        {
            using var _ = Profiler.Scope();

            if (conn is not NetPeer peer)
                return false;

            if (peer.ConnectionState != ConnectionState.Connected)
                return false;

            byte[] bytes = PacketSender.SerializePacketForSending(packet);

            // ConvertSendType only ever yields Unreliable or ReliableOrdered. ReliableOrdered
            // fragments on its own, but Unreliable is single-datagram: an oversized payload
            // throws out of peer.Send instead of being split, so the packet is lost and only
            // SendRaw's catch records it. The batching senders are the ones that hit it, since
            // aggregating is exactly what pushes a payload past one datagram. Chunk for both
            // methods the way the Riptide sender does - one path is simpler than two, and the
            // reliable case is already within the same budget.
            if (bytes.Length > MAX_PAYLOAD_BYTES && packet is not ChunkedPacket)
            {
                return SendChunked(peer, bytes, sendType);
            }

            return SendRaw(peer, bytes, packet, sendType);
        }

        private bool SendRaw(NetPeer peer, byte[] bytes, IPacket packet, PacketSendMode sendType)
        {
            DeliveryMethod deliveryMethod = ConvertSendType(sendType, packet);

            try
            {
                peer.Send(bytes, deliveryMethod);

                PacketTracker.TrackSent(new PacketTracker.PacketTrackData
                {
                    packet = packet,
                    size = bytes.Length
                });

                return true;
            }
            catch (Exception ex)
            {
                // Name the packet and its size: the previous message carried neither, so the
                // only way to find out what was being dropped was to read every call site.
                DebugConsole.LogError(
                    $"[LiteNetLibPacketSender] Failed to send {packet.GetType().Name} " +
                    $"({bytes.Length} bytes, {deliveryMethod}): {ex.Message}");
                return false;
            }
        }

        private bool SendChunked(NetPeer peer, byte[] fullData, PacketSendMode sendType)
        {
            int chunkDataSize = MAX_PAYLOAD_BYTES - 20; // overhead for ChunkedPacket header
            int totalChunks = (fullData.Length + chunkDataSize - 1) / chunkDataSize;
            int sequenceId = ChunkedPacket.GetNextSequenceId();

            for (int i = 0; i < totalChunks; i++)
            {
                int offset = i * chunkDataSize;
                int length = Math.Min(chunkDataSize, fullData.Length - offset);
                byte[] chunkData = new byte[length];
                Array.Copy(fullData, offset, chunkData, 0, length);

                var chunk = new ChunkedPacket
                {
                    SequenceId = sequenceId,
                    ChunkIndex = i,
                    TotalChunks = totalChunks,
                    ChunkData = chunkData
                };

                byte[] chunkBytes = PacketSender.SerializePacketForSending(chunk);
                if (!SendRaw(peer, chunkBytes, chunk, sendType))
                    return false;
            }

            return true;
        }

        private static DeliveryMethod ConvertSendType(PacketSendMode sendType, IPacket packet)
        {
            if (packet is ILatencySensitivePacket || (sendType & PacketSendMode.NoDelay) != 0)
                return DeliveryMethod.Unreliable;

            if ((sendType & PacketSendMode.Reliable) != 0)
                return DeliveryMethod.ReliableOrdered;

            return DeliveryMethod.Unreliable;
        }
    }
}
