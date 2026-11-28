using System;
using JetBrains.Annotations;

namespace ONI_Together_API.Transport
{
    /// <summary>
    /// Convenience base class. Implementing members as public overrides keeps them
    /// discoverable by ONI Together's reflection bridge.
    /// </summary>
    [PublicAPI]
    public abstract class TransportProviderBase : ITransportProvider
    {
        public abstract string Id { get; }
        public abstract string DisplayName { get; }

        public virtual int ApiVersion => TransportAPI.CurrentApiVersion;

        public virtual TransportCapabilities Capabilities => TransportCapabilities.None;

        public virtual TransportSetting[] GetSettings() => Array.Empty<TransportSetting>();

        public abstract ITransportServer CreateServer();

        public abstract ITransportClient CreateClient();
    }
}
