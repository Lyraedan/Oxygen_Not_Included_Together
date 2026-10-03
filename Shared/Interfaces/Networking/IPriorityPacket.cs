namespace ONI_Together.Networking.Packets.Architecture
{
	/// <summary>
	/// Marker for the few packets that steer a session rather than describe the world:
	/// hard sync, ready state, the save transfer, the host's clock. The transport sends
	/// them on its control lane (see PacketSendMode.Priority) so they are not delivered
	/// behind minutes of queued world traffic.
	/// </summary>
	public interface IPriorityPacket
	{
	}
}
