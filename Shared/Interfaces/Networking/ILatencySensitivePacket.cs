namespace ONI_Together.Networking.Packets.Architecture
{
	/// <summary>
	/// Marker for small ordered events whose application-level sequence makes it
	/// safe to bypass the optional packet-rate queue. Transport reliability still
	/// applies; only the artificial sender queue is skipped.
	/// </summary>
	public interface ILatencySensitivePacket
	{
	}
}
