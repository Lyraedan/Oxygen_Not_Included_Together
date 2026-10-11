namespace ONI_Together.Networking.Packets.Architecture
{
	/// <summary>
	/// Marker for packets a client may handle while it has no world yet (connection flow,
	/// save transfer, hard sync, chat). Everything else is dropped until the world loads.
	/// </summary>
	public interface IAllowedWithoutWorldPacket
	{
	}
}
