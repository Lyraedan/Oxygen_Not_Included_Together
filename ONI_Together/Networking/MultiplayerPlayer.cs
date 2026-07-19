using ONI_Together.Misc;
using ONI_Together.Misc.World;
using ONI_Together.Networking;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Packets.Core;
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
	public long ConnectionGeneration { get; private set; }
	private long _saveTransferGeneration;
	private bool _saveTransferActive;
	private bool _saveFallbackRequested;
	private string _saveTransferToken = string.Empty;

	private ClientReadyState _readyState;
	public ClientReadyState readyState
	{
		get => PlayerId == MultiplayerSession.HostUserID ? ClientReadyState.Ready : _readyState;
		set => _readyState = PlayerId == MultiplayerSession.HostUserID ? ClientReadyState.Ready : value;
	}

	public MultiplayerPlayer(ulong playerId)
	{
		PlayerId = playerId;
		_readyState = playerId == MultiplayerSession.HostUserID
			? ClientReadyState.Ready
			: ClientReadyState.Unready;
		ProtocolVerified = IsLocal;
		if(NetworkConfig.IsLanConfig())
		{
            PlayerName = $"Player {playerId}";
            return;
        }

		PlayerName = Utils.TrucateName(SteamFriends.GetFriendPersonaName(playerId.AsCSteamID()));
		AvatarImageId = SteamFriends.GetLargeFriendAvatar(playerId.AsCSteamID());
	}

	public long BeginConnection(object connection)
	{
		if (Equals(Connection, connection))
			return ConnectionGeneration;
		OrderedReliableChannel.DropIncoming(PlayerId);
		PacketSender.DropIncoming(PlayerId);
		WorldUpdateBatcher.DropRepairClient(PlayerId);
		DropConnectionQueues(Connection);
		Connection = connection;
		ConnectionGeneration++;
		ResetRemoteAuthority();
		return ConnectionGeneration;
	}

	public bool IsCurrentConnection(object connection, long generation)
	{
		return generation == ConnectionGeneration && Equals(Connection, connection);
	}

	public bool EndConnection(object connection, long generation)
	{
		if (!IsCurrentConnection(connection, generation))
			return false;
		DropConnectionQueues(Connection);
		OrderedReliableChannel.DropIncoming(PlayerId);
		PacketSender.DropIncoming(PlayerId);
		WorldUpdateBatcher.DropRepairClient(PlayerId);
		Connection = null;
		ConnectionGeneration++;
		ResetRemoteAuthority();
		return true;
	}

	private static void DropConnectionQueues(object connection)
	{
		if (connection == null)
			return;
		PacketSender.DropConnection(connection);
		NetworkConfig.TransportPacketSender?.DropConnection(connection);
	}

	public bool TryBeginSaveTransfer(out long generation)
	{
		generation = _saveTransferGeneration;
		if (_saveTransferActive)
			return false;
		_saveTransferActive = true;
		_saveFallbackRequested = false;
		_saveTransferToken = string.Empty;
		generation = ++_saveTransferGeneration;
		return true;
	}

	public bool TryRestartSaveTransferAfterFallback(out long generation)
	{
		generation = _saveTransferGeneration;
		if (!_saveTransferActive || !_saveFallbackRequested)
			return false;
		_saveTransferActive = true;
		_saveFallbackRequested = false;
		_saveTransferToken = string.Empty;
		generation = ++_saveTransferGeneration;
		return true;
	}

	public bool TryRestartSaveTransfer(string transferToken, out long generation)
	{
		generation = _saveTransferGeneration;
		if (!_saveTransferActive || string.IsNullOrEmpty(transferToken)
		    || !string.Equals(_saveTransferToken, transferToken, System.StringComparison.Ordinal))
		{
			return false;
		}
		_saveFallbackRequested = false;
		_saveTransferToken = string.Empty;
		generation = ++_saveTransferGeneration;
		return true;
	}

	public bool IsCurrentSaveTransfer(long generation)
		=> _saveTransferActive && generation == _saveTransferGeneration;

	public bool TrySetSaveTransferToken(long generation, string token)
	{
		if (!_saveTransferActive || generation != _saveTransferGeneration || string.IsNullOrEmpty(token))
			return false;
		_saveTransferToken = token;
		return true;
	}

	public bool TryRequestSaveFallback(string token)
	{
		if (!_saveTransferActive || _saveFallbackRequested
		    || string.IsNullOrEmpty(token) || token != _saveTransferToken)
			return false;
		_saveFallbackRequested = true;
		return true;
	}

	public void CompleteSaveTransfer()
	{
		_saveTransferActive = false;
		_saveFallbackRequested = false;
		_saveTransferToken = string.Empty;
	}

	private void ResetRemoteAuthority()
	{
		if (PlayerId != MultiplayerSession.HostUserID)
		{
			ProtocolVerified = false;
			_readyState = ClientReadyState.Unready;
		}
		CompleteSaveTransfer();
	}

	public override string ToString()
	{
		return $"{PlayerName} ({PlayerId})";
	}
}
