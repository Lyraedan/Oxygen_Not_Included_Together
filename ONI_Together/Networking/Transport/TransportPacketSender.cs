using ONI_Together.Networking.Packets.Architecture;

namespace ONI_Together.Networking.Transport
{
    public abstract class TransportPacketSender
    {
        public abstract bool SendToConnection(object conn, IPacket packet, PacketSendMode sendType = PacketSendMode.ReliableImmediate);

    }
}
