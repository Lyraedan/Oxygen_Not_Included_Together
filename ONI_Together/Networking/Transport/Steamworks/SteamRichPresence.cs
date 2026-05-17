using ONI_Together.DebugTools;
using Shared.Profiling;
using Steamworks;

namespace ONI_Together.Networking.Transport.Steamworks
{
	public static class SteamRichPresence
	{
		public static void SetStatus(string status)
		{
			using var _ = Profiler.Scope();

			if (!SteamManager.Initialized)
			{
				DebugConsole.LogWarning("SteamRichPresence: Not initialized.");
				return;
			}

			SteamFriends.SetRichPresence("gamestatus", status);
			DebugConsole.Log($"SteamRichPresence: Status set to \"{status}\"");
		}

		public static void Clear()
		{
			using var _ = Profiler.Scope();

			if (!SteamManager.Initialized)
			{
				DebugConsole.LogWarning("SteamRichPresence: Not initialized.");
				return;
			}

			SteamFriends.ClearRichPresence();
			DebugConsole.Log("SteamRichPresence: Cleared.");
		}

		public static void SetLobbyInfo(CSteamID lobby, string status)
		{
			using var _ = Profiler.Scope();

			SteamFriends.ClearRichPresence();

			SteamFriends.SetRichPresence("gamestatus", "In Multiplayer Lobby");
			SteamFriends.SetRichPresence("steam_display", "Lobby");
			SteamFriends.SetRichPresence("steam_player_group", SteamLobby.CurrentLobby.ToString());
			int group_size = SteamMatchmaking.GetNumLobbyMembers(SteamLobby.CurrentLobby);
			SteamFriends.SetRichPresence("steam_player_group_size", $"{group_size}");

		}
	}
}
