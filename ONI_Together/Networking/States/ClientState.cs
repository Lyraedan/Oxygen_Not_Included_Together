using System;

namespace ONI_Together.Networking.States
{
	public enum ClientState
	{
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
