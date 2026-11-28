using ONI_Together.DebugTools;
using ONI_Together.Networking.Transport.Drivers;
using ONI_Together.Networking.Transport.External;
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
        private static readonly Dictionary<string, ExternalTransportProvider> _externalTransports =
            new Dictionary<string, ExternalTransportProvider>(StringComparer.OrdinalIgnoreCase);
        private static ITransport _activeTransport;

        public static ITransport ActiveTransport => _activeTransport ?? GetTransport(TransportProtocol.LiteNetLib);

        static TransportRegistry()
        {
            RegisterTransport(new LiteNetLibTransport());
            RegisterTransport(new SteamworksTransport());

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

        /// <summary>
        /// Entry point used by ONI_Together_API.Transport.TransportAPI.
        /// Kept as object-based so the API assembly does not need a shared type identity.
        /// </summary>
        public static bool RegisterExternalTransport_API(object provider)
        {
            if (!ExternalTransportProvider.TryCreate(provider, out var external, out string error))
            {
                DebugConsole.LogError($"[TransportRegistry] Failed to register custom transport: {error}");
                return false;
            }

            if (_externalTransports.ContainsKey(external.Id))
            {
                DebugConsole.LogError(
                    $"[TransportRegistry] Custom transport '{external.Id}' is already registered.");
                return false;
            }

            _externalTransports[external.Id] = external;
            DebugConsole.LogSuccess(
                $"[TransportRegistry] Registered custom transport: {external.DisplayName} ({external.Id})");
            return true;
        }

        public static ExternalTransportProvider GetExternalProvider(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            return _externalTransports.TryGetValue(id, out var provider) ? provider : null;
        }

        public static IReadOnlyList<ExternalTransportProvider> GetAllExternalProviders()
        {
            return new List<ExternalTransportProvider>(_externalTransports.Values);
        }

        public static bool IsReservedTransportId(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;

            return id.Equals("steamworks", StringComparison.OrdinalIgnoreCase)
                || id.Equals("litenetlib", StringComparison.OrdinalIgnoreCase);
        }
    }
}
