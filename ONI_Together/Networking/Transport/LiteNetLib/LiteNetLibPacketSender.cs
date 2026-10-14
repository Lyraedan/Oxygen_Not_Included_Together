using LiteNetLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System;

namespace ONI_Together.Networking.Transport.Lan
{
    public class LiteNetLibPacketSender : TransportPacketSender
    {
        public override bool SendPacket(object conn, IPacket packet, PacketSendMode sendType = PacketSendMode.ReliableImmediate)
        {
            using var _ = Profiler.Scope();

            if (conn is not NetPeer peer)
                return false;

            if (peer.ConnectionState != ConnectionState.Connected)
                return false;

            byte[] bytes = PacketSender.SerializePacketForSending(packet);

            // A large priority packet (a save chunk) is split into ChunkedPackets below, which
            // do not carry the marker interface; the flag on the send mode does.
            if (packet is IPriorityPacket)
                sendType |= PacketSendMode.Priority;

            return SendChunkedIfNeeded(peer, bytes, packet, sendType, SendRaw);
        }

        private bool SendRaw(object conn, byte[] bytes, IPacket packet, PacketSendMode sendType)
        {
            if (conn is not NetPeer peer)
                return false;

            if (peer.ConnectionState != ConnectionState.Connected)
                return false;

            DeliveryMethod deliveryMethod = ConvertSendType(sendType, packet);
            byte channel = IsPriority(sendType, packet) ? PriorityChannel : DefaultChannel;

            try
            {
                peer.Send(bytes, channel, deliveryMethod);

                PacketTracker.TrackSent(new PacketTracker.PacketTrackData
                {
                    packet = packet,
                    size = bytes.Length
                });

                return true;
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[LiteNetLibPacketSender] Failed to send packet {packet.GetType().Name} ({bytes.Length} bytes): " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// LiteNetLib orders reliable packets per channel, so a second channel is a lane the
        /// world stream cannot block. Both server and client open four (ChannelsCount = 4).
        /// Unreliable sends ignore the channel number; they never queue behind reliable
        /// traffic anyway.
        /// </summary>
        private const byte DefaultChannel = 0;
        private const byte PriorityChannel = 1;

        private static bool IsPriority(PacketSendMode sendType, IPacket packet)
        {
            return (sendType & PacketSendMode.Priority) != 0 || packet is IPriorityPacket;
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
