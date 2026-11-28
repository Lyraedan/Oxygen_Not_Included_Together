using JetBrains.Annotations;

namespace ONI_Together_API.Transport
{
    /// <summary>
    /// Entry point for a custom transport mod. Register one instance with
    /// <see cref="TransportAPI.RegisterTransport"/> after all mods have loaded.
    /// </summary>
    [PublicAPI]
    public interface ITransportProvider
    {
        /// <summary>Stable, unique identifier, for example "riptide".</summary>
        string Id { get; }

        /// <summary>Name shown in the Custom Transport tab.</summary>
        string DisplayName { get; }

        /// <summary>Contract version implemented by this provider. The current version is 1.</summary>
        int ApiVersion { get; }

        TransportCapabilities Capabilities { get; }

        /// <summary>User-editable settings shown in the Custom Transport host/join tabs.</summary>
        TransportSetting[] GetSettings();

        ITransportServer CreateServer();

        ITransportClient CreateClient();
    }
}
