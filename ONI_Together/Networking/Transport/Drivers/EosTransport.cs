using ONI_Together.DebugTools;
using ONI_Together.Networking.Transport.Lan;

namespace ONI_Together.Networking.Transport.Drivers
{
    /// <summary>
    /// Transport driver for Epic Online Services (EOS) P2P crossplay.
    /// </summary>
    public class EosTransport : ITransport
    {
        public TransportProtocol Protocol => TransportProtocol.EOS;
        public string DisplayName => "Epic Online Services (Crossplay)";
        public bool SupportsNativeFragmentation => true;
        public bool SupportsNatTraversal => true;

        public TransportServer CreateServer()
        {
            DebugConsole.LogWarning("[EosTransport] EOS transport server using LiteNetLib backend fallback until EOS P2P session initialized.");
            return new LiteNetLibServer();
        }

        public TransportClient CreateClient()
        {
            DebugConsole.LogWarning("[EosTransport] EOS transport client using LiteNetLib backend fallback until EOS P2P session initialized.");
            return new LiteNetLibClient();
        }

        public TransportPacketSender CreatePacketSender()
        {
            return new LiteNetLibPacketSender();
        }

        public void Initialize()
        {
            DebugConsole.Log("[EosTransport] Initialized EOS Transport Driver.");
        }

        public void Shutdown() { }
    }
}
