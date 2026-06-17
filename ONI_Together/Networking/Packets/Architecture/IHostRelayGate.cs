namespace ONI_Together.Networking.Packets.Architecture
{
	/// <summary>
	/// Implemented by packets that the host must be able to veto at the relay choke point
	/// (<see cref="ONI_Together.Networking.Packets.Core.HostBroadcastPacket"/>). When a
	/// client wraps a packet for the host to fan out, the host first asks
	/// <see cref="HostShouldProcess"/>; if it returns false the host neither applies the
	/// packet locally nor rebroadcasts it to the other clients.
	///
	/// This exists because the resume gate (see SpeedChangePacket / SpeedControlPatch) lived
	/// inside the inner packet's OnDispatched and only protected the host's local apply — the
	/// generic relay would still fan a rejected resume out to every other client, letting one
	/// client flip the others' pause state while the host stayed paused. Gating here covers
	/// both apply and fan-out from a single authoritative point.
	/// </summary>
	public interface IHostRelayGate
	{
		/// <summary>
		/// HOST - return false to have the relay drop this packet entirely (no local apply,
		/// no rebroadcast).
		/// </summary>
		bool HostShouldProcess();
	}
}
