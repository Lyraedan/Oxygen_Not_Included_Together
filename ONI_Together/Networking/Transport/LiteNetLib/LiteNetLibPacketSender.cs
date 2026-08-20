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
                DebugConsole.LogError("[LiteNetLibPacketSender] Failed to send packet: " + ex.Message);
                return false;
            }
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
