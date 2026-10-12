using System;
using Steamworks;

namespace ONI_Together.Networking.Transport.Steamworks
{
	internal sealed class PendingLobbyJoinCallback
	{
		private Action<CSteamID> callback;

		public void Register(Action<CSteamID> onJoined)
		{
			callback = onJoined;
		}

		public Action<CSteamID> Consume()
		{
			// Release the old screen before invocation, including when it throws or registers another join.
			var onJoined = callback;
			callback = null;
			return onJoined;
		}

		public void Clear()
		{
			callback = null;
		}
	}
}
