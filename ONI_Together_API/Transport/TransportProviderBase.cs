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

        /// <summary>
        /// True when the transport needs a user login before it can host or join.
        /// Existing providers default to false.
        /// </summary>
        public virtual bool RequiresLogin => false;

        /// <summary>Current login state for providers that require a login.</summary>
        public virtual TransportLoginState LoginState => TransportLoginState.Ready;

        /// <summary>Human-readable login status shown in the Custom tab.</summary>
        public virtual string LoginMessage => string.Empty;

        /// <summary>Starts or retries the login flow. Safe to call repeatedly.</summary>
        public virtual void BeginLogin() { }

        public virtual TransportSetting[] GetSettings() => Array.Empty<TransportSetting>();

        public abstract ITransportServer CreateServer();

        public abstract ITransportClient CreateClient();
    }
}
