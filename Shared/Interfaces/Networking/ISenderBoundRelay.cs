namespace Shared.Interfaces.Networking
{
	/// <summary>
	/// Exposes the player identity carried by a relay payload so the host can
	/// bind it to the authenticated transport sender before dispatching it.
	/// </summary>
	public interface ISenderBoundRelay
	{
		ulong RelaySenderId { get; }
	}
}
