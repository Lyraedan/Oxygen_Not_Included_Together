using System;

namespace ONI_Together.Networking.Transport
{
    public enum TransportProtocol
    {
        Steamworks = 0,
        LiteNetLib = 1,
        Riptide = 2
    }

    /// <summary>
    /// Standardized transport interface abstracting low-level networking technologies
    /// (Steamworks P2P, LiteNetLib UDP, Riptide) from higher-level gameplay systems.
    /// </summary>
    public interface ITransport
    {
        TransportProtocol Protocol { get; }
        string DisplayName { get; }
        bool SupportsNativeFragmentation { get; }
        bool SupportsNatTraversal { get; }

        TransportServer CreateServer();
        TransportClient CreateClient();
        TransportPacketSender CreatePacketSender();

        void Initialize();
        void Shutdown();
    }
}
