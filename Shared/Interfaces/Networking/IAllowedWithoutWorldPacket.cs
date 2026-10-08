namespace ONI_Together.Networking.Packets.Architecture
{
	/// <summary>
	/// Marks packets safe to dispatch without a loaded world and during synchronization.
	/// Do not mark wrappers that directly dispatch arbitrary gameplay packets.
	/// </summary>
	public interface IAllowedWithoutWorldPacket
	{
	}
}
