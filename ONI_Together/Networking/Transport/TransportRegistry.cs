using ONI_Together.DebugTools;
using ONI_Together.Networking.Transport.Drivers;
using System;
using System.Collections.Generic;

namespace ONI_Together.Networking.Transport
{
    /// <summary>
    /// Central registry managing available network transport drivers.
    /// </summary>
    public static class TransportRegistry
    {
        private static readonly Dictionary<TransportProtocol, ITransport> _transports = new Dictionary<TransportProtocol, ITransport>();
        private static ITransport _activeTransport;

        public static ITransport ActiveTransport => _activeTransport ?? GetTransport(TransportProtocol.LiteNetLib);

        static TransportRegistry()
        {
            RegisterTransport(new LiteNetLibTransport());
            RegisterTransport(new SteamworksTransport());
            RegisterTransport(new RiptideTransport());

            _activeTransport = _transports[TransportProtocol.LiteNetLib];
        }

        public static void RegisterTransport(ITransport transport)
        {
            if (transport == null) return;
            _transports[transport.Protocol] = transport;
            DebugConsole.Log($"[TransportRegistry] Registered transport driver: {transport.DisplayName} ({transport.Protocol})");
        }

        public static ITransport GetTransport(TransportProtocol protocol)
        {
            if (_transports.TryGetValue(protocol, out var transport))
                return transport;

            DebugConsole.LogWarning($"[TransportRegistry] Transport {protocol} not found, falling back to LiteNetLib.");
            return _transports[TransportProtocol.LiteNetLib];
        }

        public static bool SetActiveTransport(TransportProtocol protocol)
        {
            var transport = GetTransport(protocol);
            if (transport == null) return false;

            _activeTransport = transport;
            _activeTransport.Initialize();
            DebugConsole.Log($"[TransportRegistry] Active transport set to: {_activeTransport.DisplayName}");
            return true;
        }

        public static IEnumerable<ITransport> GetAllTransports()
        {
            return _transports.Values;
        }
    }
}
