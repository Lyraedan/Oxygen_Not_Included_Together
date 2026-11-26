using ONI_Together.Networking;

namespace ONI_Together.Misc
{
	/// <summary>
	/// Snapshot of the current game state used by every rich presence backend.
	/// Kept free of UI/timing code so Discord and Steam always show the same information.
	/// </summary>
	public readonly struct PresenceInfo
	{
		public readonly string Colony;
		public readonly int Cycle;
		public readonly int Dupes;
		public readonly bool InMenu;
		public readonly bool InGame;
		public readonly bool InSession;
		public readonly bool IsHost;
		public readonly bool SteamTransport;
		public readonly string Asteroid;

		public PresenceInfo(
			string colony,
			int cycle,
			int dupes,
			bool inMenu,
			bool inGame,
			bool inSession,
			bool isHost,
			bool steamTransport,
			string asteroid)
		{
			Colony = colony ?? "";
			Cycle = cycle;
			Dupes = dupes;
			InMenu = inMenu;
			InGame = inGame;
			InSession = inSession;
			IsHost = isHost;
			SteamTransport = steamTransport;
			Asteroid = asteroid ?? "";
		}

		public string Details
		{
			get
			{
				if (!InSession && !InGame)
					return InMenu ? "At main menu" : "";

				string prefix = string.IsNullOrEmpty(Colony) ? "" : Colony + " — ";
				return $"{prefix}Cycle {Cycle} with {Dupes} dupes";
			}
		}

		public string State
		{
			get
			{
				if (InSession)
					return $"{(IsHost ? "Hosting" : "Playing")} over {(SteamTransport ? "Steam" : "LAN")}";

				return InGame ? "Playing singleplayer" : "";
			}
		}

		public static PresenceInfo Capture(string asteroid = null)
		{
			int cycle = GameClock.Instance != null ? GameClock.Instance.GetCycle() + 1 : 1;
			int dupes = global::Components.LiveMinionIdentities?.Count ?? 0;
			string colony = SaveGame.Instance?.BaseName ?? "";

			return new PresenceInfo(
				colony,
				cycle,
				dupes,
				Utils.IsInMenu(),
				Utils.IsInGame(),
				MultiplayerSession.InActiveSession,
				MultiplayerSession.IsHost,
				NetworkConfig.IsSteamConfig(),
				asteroid);
		}
	}
}
