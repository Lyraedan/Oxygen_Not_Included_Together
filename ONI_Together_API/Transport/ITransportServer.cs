using System.Collections.Generic;
using JetBrains.Annotations;

namespace ONI_Together_API.Transport
{
    /// <summary>
    /// Host-side raw byte transport. ONI Together owns packet serialization, framing,
    /// chunking and dispatch; this object only moves bytes and tracks peers.
    /// For the optional direct save path, advertise
    /// <see cref="TransportCapabilities.TcpFileTransfer"/> and expose a public
    /// <c>int GetFileTransferPort()</c> method (the base class provides a default).
    /// </summary>
    [PublicAPI]
    public interface ITransportServer
    {
        /// <summary>The host's own player id.</summary>
        ulong LocalPlayerId { get; }

        /// <summary>Start listening. Called on the Unity main thread.</summary>
        void Start(Dictionary<string, string> settings);

        void Stop();

        /// <summary>Poll the native transport. Called on the Unity main thread.</summary>
        void Update();

        /// <summary>
        /// Dequeue one received packet. Return false when the queue is empty.
        /// <paramref name="senderId"/> is the player id of the remote peer.
        /// </summary>
        bool TryDequeueReceived(out ulong senderId, out byte[] payload);

        /// <summary>
        /// All connected player ids, including <see cref="LocalPlayerId"/>.
        /// </summary>
        ulong[] GetConnectedPlayerIds();

        /// <summary>
        /// Send a raw, already-framed packet payload to one player.
        /// <paramref name="sendMode"/> uses the ONI Together PacketSendMode flags.
        /// </summary>
        bool Send(ulong playerId, byte[] payload, int sendMode);

        void Kick(ulong playerId);

        /// <summary>Last fatal error, or an empty string.</summary>
        string LastError { get; }
    }
}
