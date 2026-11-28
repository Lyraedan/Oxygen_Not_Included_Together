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

        /// <summary>True when this provider can list lobbies in the server browser.</summary>
        public virtual bool SupportsLobbyBrowser => false;

        /// <summary>True when this provider can apply Public/FriendsOnly/Private visibility.</summary>
        public virtual bool SupportsLobbyVisibility => false;

        /// <summary>Supplies host lobby metadata (name, visibility, password, size).</summary>
        public virtual void SetLobbyAdvertisement(TransportLobbyAdvertisement advertisement) { }

        /// <summary>Changes the visibility of the currently hosted lobby.</summary>
        public virtual void SetLobbyVisibility(TransportLobbyVisibility visibility) { }

        /// <summary>Starts or refreshes an asynchronous lobby search.</summary>
        public virtual void RefreshLobbyList() { }

        /// <summary>Returns the most recently received lobby list, or an empty array.</summary>
        public virtual TransportLobbyInfo[] GetLobbyList() => Array.Empty<TransportLobbyInfo>();

        public virtual TransportSetting[] GetSettings() => Array.Empty<TransportSetting>();

        public abstract ITransportServer CreateServer();

        public abstract ITransportClient CreateClient();
    }
}
