using System.Collections.Generic;
using JetBrains.Annotations;

namespace ONI_Together_API.Transport
{
    /// <summary>
    /// Convenience base class for client-side transports.
    /// </summary>
    [PublicAPI]
    public abstract class TransportClientBase : ITransportClient
    {
        public abstract ulong LocalPlayerId { get; }
        public abstract ulong HostPlayerId { get; }
        public abstract bool IsConnected { get; }

        public abstract void Connect(Dictionary<string, string> settings);

        public abstract void Disconnect();

        public virtual void Reconnect(Dictionary<string, string> settings)
        {
            Disconnect();
            Connect(settings);
        }

        public abstract void Update();

        public abstract bool TryDequeueReceived(out ulong senderId, out byte[] payload);

        public abstract bool Send(ulong playerId, byte[] payload, int sendMode);

        public virtual string LastDisconnectReason => string.Empty;
        public virtual string LastDisconnectMessage => string.Empty;

        public virtual int GetPing() => -1;
        public virtual int GetJitterState() => 0;
        public virtual int GetLatencyState() => 0;
        public virtual int GetPacketLossState() => 0;
        public virtual int GetServerPerformanceState() => 0;
    }
}
