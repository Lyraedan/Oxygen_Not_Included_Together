namespace ONI_Together.Networking.States
{
	public enum ClientEvent
	{
		None = 0,
		BeginConnect,
		CancelConnect,
		ConnectionFailed,
		TransportConnected,
		WorldLoadStarted,
		ConnectionFlowCompleted,
		TransportDisconnected
	}
}
