using ONI_Together.Networking.Transport.Lan;

namespace ONI_Together.Networking.Transport.Drivers
{
    public class LiteNetLibTransport : ITransport
    {
        public TransportProtocol Protocol => TransportProtocol.LiteNetLib;
        public string DisplayName => "LiteNetLib (LAN / Direct IP)";
        public bool SupportsNativeFragmentation => true;
        public bool SupportsNatTraversal => false;

        public TransportServer CreateServer() => new LiteNetLibServer();
        public TransportClient CreateClient() => new LiteNetLibClient();
        public TransportPacketSender CreatePacketSender() => new LiteNetLibPacketSender();

        public void Initialize() { }
        public void Shutdown() { }
    }
}
