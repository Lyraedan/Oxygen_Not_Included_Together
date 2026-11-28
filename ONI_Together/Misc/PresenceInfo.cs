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
		public readonly string TransportName;
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
			string transportName,
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
			TransportName = string.IsNullOrEmpty(transportName) ? "LAN" : transportName;
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
					return $"{(IsHost ? "Hosting" : "Playing")} over {TransportName}";

				return InGame ? "Playing singleplayer" : "";
			}
		}

		public static PresenceInfo Capture(string asteroid = null)
		{
			int cycle = GameClock.Instance != null ? GameClock.Instance.GetCycle() + 1 : 1;
			int dupes = global::Components.LiveMinionIdentities?.Count ?? 0;
			string colony = SaveGame.Instance?.BaseName ?? "";
			string transportName = GetTransportName();

			return new PresenceInfo(
				colony,
				cycle,
				dupes,
				Utils.IsInMenu(),
				Utils.IsInGame(),
				MultiplayerSession.InActiveSession,
				MultiplayerSession.IsHost,
				NetworkConfig.IsSteamConfig(),
				transportName,
				asteroid);
		}

		private static string GetTransportName()
		{
			if (NetworkConfig.IsSteamConfig())
				return "Steam";

			if (NetworkConfig.IsCustomConfig)
			{
				var provider = NetworkConfig.ActiveExternalProvider;
				if (provider != null && !string.IsNullOrEmpty(provider.DisplayName))
					return provider.DisplayName;

				if (!string.IsNullOrEmpty(NetworkConfig.TransportId))
					return NetworkConfig.TransportId;
			}

			return "LAN";
		}
	}
}
