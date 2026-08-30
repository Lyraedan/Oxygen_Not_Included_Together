using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.States;
using Steamworks;

public class MultiplayerPlayer
{
	public ulong PlayerId { get; private set; }
	public string PlayerName { get; set; }
	public bool IsLocal => PlayerId == NetworkConfig.GetLocalID();

	public int AvatarImageId { get; private set; } = -1;
	//public HSteamNetConnection? Connection { get; set; } = null;
	public object? Connection { get; set; } = null;
	public bool IsConnected => Connection != null;
	public bool ProtocolVerified { get; set; }

	// Default to Unready: a freshly created/recreated player (e.g. on connect or
	// reconnect-for-load) must never read as ready until it explicitly says so.
	public ClientReadyState readyState = ClientReadyState.Unready;

    public MultiplayerPlayer(ulong playerId)
	{
		PlayerId = playerId;
		ProtocolVerified = IsLocal;
		if(NetworkConfig.IsLanConfig())
		{
            PlayerName = $"Player {playerId}";
            return;
        }

		PlayerName = Utils.TrucateName(SteamFriends.GetFriendPersonaName(playerId.AsCSteamID()));
		AvatarImageId = SteamFriends.GetLargeFriendAvatar(playerId.AsCSteamID());
	}

	public override string ToString()
	{
		return $"{PlayerName} ({PlayerId})";
	}
}
