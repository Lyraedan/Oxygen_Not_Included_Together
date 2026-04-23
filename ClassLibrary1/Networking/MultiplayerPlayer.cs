using ONI_MP.Misc;
using ONI_MP.Networking;
using ONI_MP.Networking.States;
using Steamworks;

namespace ONI_MP.Networking
{
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

	public ClientReadyState readyState = ClientReadyState.Ready;

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

}