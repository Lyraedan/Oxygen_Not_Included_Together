using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.UI;
using Steamworks;
using System;

namespace ONI_Together.Networking.Transport.Steamworks
{
	public static partial class SteamLobby
	{
		private static Callback<LobbyDataUpdate_t> _lobbyDataUpdate;
		private static CSteamID _pendingLobbyData = CSteamID.Nil;
		private static Action<CSteamID, bool> _onLobbyDataReceived;

		private static void InitializeLobbyDataCallback()
			=> _lobbyDataUpdate = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);

		internal static bool RequestLobbyMetadata(
			CSteamID lobbyId,
			Action<CSteamID, bool> onReceived)
		{
			if (!SteamManager.Initialized || !lobbyId.IsValid() || onReceived == null)
			{
				onReceived?.Invoke(lobbyId, false);
				return false;
			}

			_pendingLobbyData = lobbyId;
			_onLobbyDataReceived = onReceived;
			if (SteamMatchmaking.RequestLobbyData(lobbyId))
				return true;

			CompleteLobbyDataRequest(false);
			return false;
		}

		private static void OnLobbyDataUpdate(LobbyDataUpdate_t data)
		{
			if (data.m_ulSteamIDLobby == _pendingLobbyData.m_SteamID)
				CompleteLobbyDataRequest(data.m_bSuccess != 0);
		}

		private static void CompleteLobbyDataRequest(bool success)
		{
			CSteamID lobbyId = _pendingLobbyData;
			Action<CSteamID, bool> callback = _onLobbyDataReceived;
			_pendingLobbyData = CSteamID.Nil;
			_onLobbyDataReceived = null;
			callback?.Invoke(lobbyId, success);
		}

		private static void OnLobbyJoinRequested(GameLobbyJoinRequested_t request)
		{
			DebugConsole.Log($"[SteamLobby] Joining lobby invited by {request.m_steamIDFriend}");
			NetworkConfig.UpdateTransport(NetworkConfig.NetworkTransport.STEAMWORKS);
			RequestLobbyMetadata(request.m_steamIDLobby, HandleInvitedLobbyMetadata);
		}

		private static void HandleInvitedLobbyMetadata(CSteamID lobbyId, bool success)
		{
			if (!success || !TryGetLobbyPasswordRequirement(lobbyId, out bool requiresPassword))
			{
				DebugConsole.LogWarning($"[SteamLobby] Could not read access metadata for lobby {lobbyId}");
				return;
			}

			if (requiresPassword)
				UnityPasswordInputDialogueUI.ShowPasswordDialogueFor(lobbyId.m_SteamID);
			else
				JoinLobby(lobbyId);
		}

		internal static bool TryGetLobbyPasswordRequirement(
			CSteamID lobbyId,
			out bool requiresPassword)
			=> TryParsePasswordRequirement(
				SteamMatchmaking.GetLobbyData(lobbyId, "has_password"),
				out requiresPassword);

		internal static bool TryParsePasswordRequirement(string value, out bool requiresPassword)
		{
			requiresPassword = value == "1";
			return requiresPassword || value == "0";
		}

		internal static bool HasConfiguredPassword(LobbySettings settings)
			=> settings != null
			   && settings.RequirePassword
			   && PasswordHelper.HasPassword(settings.PasswordHash);
	}
}
