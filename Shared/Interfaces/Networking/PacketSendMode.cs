using System;

namespace ONI_Together.Networking
{
    /// <summary>
    /// Describes how a packet should be delivered across the network.
    /// Transport-agnostic abstraction that maps to the underlying networking backend (e.g. Steam, Riptide).
    /// <para />
    /// Based off Steam networking types: https://partner.steamgames.com/doc/api/steamnetworkingtypes
    /// </summary>
    [Flags]
	public enum PacketSendMode
	{
        /// <summary>
        /// Sends the packet unreliably.
        /// Delivery is not guaranteed and packets may be lost or arrive out of order.
        /// Suitable for frequently updated data such as position or state snapshots.
        /// </summary>
        Unreliable = 0,

        /// <summary>
        /// Disables packet buffering/coalescing for this send.
        /// The packet will be flushed immediately instead of waiting to be grouped with others.
        /// Use sparingly, typically for latency-sensitive messages.
        /// </summary>
        Immediate = 1,

        /// <summary>
        /// Sends the packet unreliably and flushes it immediately,
        /// bypassing any buffering or coalescing behavior.
        /// </summary>
        UnreliableImmediate = Unreliable | Immediate,

        /// <summary>
        /// Drops the packet if it cannot be sent quickly.
        /// Useful for time-sensitive data that should not be delayed (e.g. voice or real-time updates).
        /// Only applicable to unreliable sends.
        /// </summary>
        NoDelay = 4,

        /// <summary>
        /// Sends the packet unreliably, without buffering, and drops it
        /// if it cannot be transmitted within a short time window.
        /// Ideal for real-time data where outdated information is not useful.
        /// </summary>
        UnreliableNoDelay = Unreliable | NoDelay | Immediate,

        /// <summary>
        /// Sends the packet reliably.
        /// Delivery and ordering are guaranteed, but may introduce additional latency.
        /// Suitable for critical data such as game events or state changes.
        /// </summary>
        Reliable = 8,

        /// <summary>
        /// Sends the packet reliably and flushes it immediately,
        /// bypassing buffering to reduce latency.
        /// </summary>
        ReliableImmediate = Reliable | Immediate
    }
}
