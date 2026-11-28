using System.Collections.Generic;
using JetBrains.Annotations;

namespace ONI_Together_API.Transport
{
    /// <summary>
    /// Client-side raw byte transport. ONI Together owns packet serialization, framing,
    /// chunking and dispatch; this object only moves bytes.
    /// For the optional direct save path, advertise
    /// <see cref="TransportCapabilities.TcpFileTransfer"/> and expose a public
    /// <c>string GetFileTransferHost()</c> method (the base class provides a default).
    /// </summary>
    [PublicAPI]
    public interface ITransportClient
    {
        ulong LocalPlayerId { get; }
        ulong HostPlayerId { get; }
        bool IsConnected { get; }

        /// <summary>Begin connecting. Called on the Unity main thread.</summary>
        void Connect(Dictionary<string, string> settings);

        void Disconnect();

        /// <summary>Reconnect using the same settings, after a transient drop.</summary>
        void Reconnect(Dictionary<string, string> settings);

        /// <summary>Poll the native transport. Called on the Unity main thread.</summary>
        void Update();

        /// <summary>
        /// Dequeue one received packet. Return false when the queue is empty.
        /// <paramref name="senderId"/> is the player id of the remote peer.
        /// </summary>
        bool TryDequeueReceived(out ulong senderId, out byte[] payload);

        /// <summary>Send a raw, already-framed packet payload (normally to the host).</summary>
        bool Send(ulong playerId, byte[] payload, int sendMode);

        string LastDisconnectReason { get; }
        string LastDisconnectMessage { get; }

        int GetPing();
        int GetJitterState();
        int GetLatencyState();
        int GetPacketLossState();
        int GetServerPerformanceState();
    }
}
