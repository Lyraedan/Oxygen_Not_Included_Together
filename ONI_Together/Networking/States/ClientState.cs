using System;

namespace ONI_Together.Networking.States
{
	public enum ClientState
	{
		Error = -1,
		Disconnected,
		Connecting,
		Connected,
		LoadingWorld,
		InGame
	}

	[Flags]
	public enum ClientReadyState
	{
		Ready,
		Unready,
		Loading
	}
}
