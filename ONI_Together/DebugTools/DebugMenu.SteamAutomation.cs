#if DEBUG
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Transport.Steamworks;
using System;

namespace ONI_Together.DebugTools
{
	public partial class DebugMenu
	{
		private const string SteamJoinCommandPrefix = "steam-join:";

		internal static bool TryParseSteamJoinCommand(string command, out string lobbyCode)
		{
			lobbyCode = string.Empty;
			if (command?.StartsWith(SteamJoinCommandPrefix, StringComparison.Ordinal) != true)
				return false;

			string candidate = LobbyCodeHelper.CleanCode(
				command.Substring(SteamJoinCommandPrefix.Length));
			if (!LobbyCodeHelper.IsValidCodeFormat(candidate)
			    || !LobbyCodeHelper.TryParseCode(candidate, out _))
				return false;

			lobbyCode = candidate;
			return true;
		}

		private static DebugCommandOutcome StartConfiguredSteamHost()
		{
			if (Utils.IsInGame() && MultiplayerSession.InSession)
				return MultiplayerSession.IsHostInSession
					? DebugCommandOutcome.Ok("steam-host", "already-hosting")
					: DebugCommandOutcome.Fail("steam-host", "session-already-active");
			if (!Utils.IsInGame() && !Utils.IsInMenu())
				return DebugCommandOutcome.Fail("steam-host", "main-menu-or-world-required");

			Configuration.Instance.Host.NetworkTransport =
				(int)NetworkConfig.NetworkTransport.STEAMWORKS;
			Configuration.Instance.Host.Lobby.IsPrivate = true;
			Configuration.Instance.Host.Lobby.RequirePassword = false;
			Configuration.Instance.Host.Lobby.PasswordHash = string.Empty;
			NetworkConfig.UpdateTransport(NetworkConfig.NetworkTransport.STEAMWORKS);
			Configuration.Instance.Save();

			if (Utils.IsInGame())
			{
				NetworkConfig.StartServer();
				return DebugCommandOutcome.Ok("steam-host", "start-requested");
			}

			string latestSave = SaveLoader.GetLatestSaveForCurrentDLC();
			if (string.IsNullOrEmpty(latestSave) || !System.IO.File.Exists(latestSave))
				return DebugCommandOutcome.Fail("steam-host", "latest-save-not-found");
			MultiplayerSession.ShouldHostAfterLoad = true;
			KCrashReporter.MOST_RECENT_SAVEFILE = latestSave;
			SaveLoader.SetActiveSaveFilePath(latestSave);
			App.LoadScene("backend");
			return DebugCommandOutcome.Ok("steam-host", "load-and-host-requested");
		}

		private static DebugCommandOutcome StartConfiguredSteamJoin(string lobbyCode)
		{
			if (!Utils.IsInMenu())
				return DebugCommandOutcome.Fail("steam-join", "main-menu-required");

			NetworkConfig.UpdateTransport(NetworkConfig.NetworkTransport.STEAMWORKS);
			SteamLobby.JoinLobbyByCode(
				lobbyCode,
				onError: reason => DebugConsole.LogWarning(
					$"[DebugCommand][FAIL] command=steam-join reason={reason}"));
			return DebugCommandOutcome.Ok("steam-join", "connect-requested");
		}
	}
}
#endif
