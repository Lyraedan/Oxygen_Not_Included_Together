using System.Collections.Generic;
using JetBrains.Annotations;

namespace ONI_Together_API.Transport
{
    /// <summary>
    /// Convenience base class for host-side transports.
    /// </summary>
    [PublicAPI]
    public abstract class TransportServerBase : ITransportServer
    {
        public abstract ulong LocalPlayerId { get; }

        public abstract void Start(Dictionary<string, string> settings);

        public abstract void Stop();

        public abstract void Update();

        public abstract bool TryDequeueReceived(out ulong senderId, out byte[] payload);

        public abstract ulong[] GetConnectedPlayerIds();

        public abstract bool Send(ulong playerId, byte[] payload, int sendMode);

        public virtual void Kick(ulong playerId) { }

        public virtual string LastError => string.Empty;

        /// <summary>
        /// Optional TCP port where the host serves the save file when
        /// <see cref="TransportCapabilities.TcpFileTransfer"/> is advertised.
        /// Return 0 to disable the direct TCP path.
        /// </summary>
        public virtual int GetFileTransferPort() => 0;
    }
}
