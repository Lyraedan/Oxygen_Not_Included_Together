using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport.Steamworks;
using Steamworks;
using Shared.Profiling;

namespace ONI_Together.Networking
{
	public class ReadyManager
	{

		public static void SetupListeners()
		{
			using var _ = Profiler.Scope();

			SteamLobby.OnLobbyMembersRefreshed += UpdateReadyStateTracking;
		}

		/// <summary>
		/// HOST - shared "a client (re)connected" resync, called from every transport's connect
		/// callback. The caller must mark the (re)connecting player Unready first.
		/// </summary>
		public static void HandleClientConnected()
		{
			using var _ = Profiler.Scope();

			// The host's own loopback connect on LAN host-start happens before the session is
			// established; PauseSimForReadyScreen's own InActiveSession guard drops that one.
			Utils.PauseSimForReadyScreen();

			RefreshScreen();
			RefreshReadyState();
		}

		public static void SendAllReadyPacket()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;

			PacketSender.SendToAllClients(new AllClientsReadyPacket());
			AllClientsReadyPacket.ProcessAllReady();
		}

		public static void SendStatusUpdatePacketToClients()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;

			string text = GetScreenText();
			var packet = new ClientReadyStatusUpdatePacket
			{
				Message = text
			};
			PacketSender.SendToAllClients(packet);
		}

		public static void SendReadyStatusPacket(ClientReadyState state)
		{
			using var _ = Profiler.Scope();

			// Host is always considered ready so it doesn't send these
			if (MultiplayerSession.IsHost)
				return;

			var packet = new ClientReadyStatusPacket
			{
				SenderId = NetworkConfig.GetLocalID(),
				Status = state,
				PlayerName = Utils.GetLocalPlayerName()
			};
			PacketSender.SendToHost(packet);

			// Log every send: Loading goes out just before this client drops its
			// connection to load, and Ready is the only thing that opens the host's
			// gate after the reload.
			DebugConsole.Log($"[ReadyManager] Sent {state} notice to host as {packet.SenderId}");
		}

		public static void MarkAllAsUnready()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;

			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				// IsConsideredReady short-circuits on the host id, so its stored state is dead.
				if (player.PlayerId == MultiplayerSession.HostUserID)
					continue;

				player.readyState = ClientReadyState.Unready;
			}
			RefreshScreen();
		}

		public static void SetPlayerReadyState(MultiplayerPlayer player, ClientReadyState state)
		{
			using var _ = Profiler.Scope();

			if (player.PlayerId == MultiplayerSession.HostUserID)
				return;

			player.readyState = state;
		}

		public static void RefreshScreen()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession)
				return;

			MultiplayerOverlay.Show(GetScreenText());
		}

		/// <summary>
		/// The whole overlay body, suffix included - the host's own screen and the copy shipped
		/// to clients in <see cref="ClientReadyStatusUpdatePacket"/> must read the same.
		/// </summary>
		private static string GetScreenText()
		{
			using var _ = Profiler.Scope();

			int readyCount = GetReadyCount();
			// A client mid load-reconnect is off the roster but still expected, so add it to the
			// total — otherwise the overlay reads "2/2" while we are still waiting on the loader.
			int pendingLoads = NetworkConfig.TransportServer?.PendingLoadingClientCount ?? 0;
			int maxPlayers = MultiplayerSession.ConnectedPlayers.Values.Count + pendingLoads;
			string message = string.Format(STRINGS.UI.MP_OVERLAY.SYNC.WAITING_FOR_PLAYERS_SYNC, readyCount, maxPlayers);
			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				ClientReadyState displayState = IsConsideredReady(player)
					? ClientReadyState.Ready
					: ClientReadyState.Unready;
				message += $"{player.PlayerName}: {GetReadyText(displayState)}\n";
			}

			// Say why the tools stopped responding, or a silent refusal reads as a frozen game.
			return message + STRINGS.UI.MP_OVERLAY.SYNC.INPUT_LOCKED_WHILE_WAITING;
		}

		/// <summary>
		/// Single source of truth for "is this player ready" - overlay text, ready count and
		/// resume gate. The host always reads ready regardless of its stored flag.
		/// NOTE: a disconnected client (Connection == null) is deliberately NOT treated as ready.
		/// Clients drop their socket precisely *while loading the level*, so it cannot be told
		/// apart from a crash; one that truly left is removed from ConnectedPlayers instead.
		/// </summary>
		private static bool IsConsideredReady(MultiplayerPlayer player)
		{
			if (player.PlayerId == MultiplayerSession.HostUserID)
				return true;

			return player.readyState == ClientReadyState.Ready;
		}

		private static int GetReadyCount()
		{
			using var _ = Profiler.Scope();

			int count = 0;
			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (IsConsideredReady(player))
					count++;
			}
			return count;
		}

		private static string GetReadyText(ClientReadyState readyState)
		{
			using var _ = Profiler.Scope();

			switch (readyState)
			{
				case ClientReadyState.Ready:
					return STRINGS.UI.MP_OVERLAY.SYNC.READYSTATE.READY;
				case ClientReadyState.Unready:
					return STRINGS.UI.MP_OVERLAY.SYNC.READYSTATE.UNREADY;
			}
			return STRINGS.UI.MP_OVERLAY.SYNC.READYSTATE.UNKNOWN;
		}

		private static void UpdateReadyStateTracking(CSteamID id)
		{
			using var _ = Profiler.Scope();

			DebugConsole.LogAssert($"Update ready state tracking for {id}");
			if (!MultiplayerSession.IsHost)
				return;
			if (MultiplayerOverlay.IsOpen)
				RefreshScreen();
		}

		/// <summary>
		/// The authority gate for resuming the sim: the host may only resume when every connected
		/// player is ready. This is the real safety — UI visibility must never permit resume.
		/// </summary>
		public static bool CanHostResume()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession)
				return true;

			// Both LAN transports remove a client from ConnectedPlayers when it disconnects to
			// load the level, so IsEveryoneReady stops seeing it and the gate would wrongly open
			// mid-load. Steamworks instead keeps a Connection==null placeholder in the roster, so
			// it reports no pending loads and is covered by IsEveryoneReady below.
			if (NetworkConfig.TransportServer?.HasPendingLoadingClients == true)
				return false;

			return IsEveryoneReady();
		}

		/// <summary>
		/// HOST ONLY - Check if all connected clients are ready
		/// </summary>
		public static bool IsEveryoneReady()
		{
			using var _ = Profiler.Scope();

			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (!IsConsideredReady(player))
					return false;
			}
			return true;
		}

		internal static void RefreshReadyState()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession)
				return;

			if (MultiplayerSession.IsQuitting)
				return;

			// A client mid load-reconnect is off the roster but not gone: don't take the "only
			// host left -> all ready" shortcut, or the gate opens before it finishes loading.
			int pendingLoads = NetworkConfig.TransportServer?.PendingLoadingClientCount ?? 0;
			bool canResume = CanHostResume();

			// Whether the host counts itself in ConnectedPlayers differs per transport:
			// LiteNetLibServer inserts it as ConnectedPlayers[1]; a Steam host is absent from
			// its own roster (measured - one client connected logs "roster: 1, others: 1").
			// Raw roster size therefore cannot answer "is anyone else here", and reading it
			// that way let a Steam host with one client resume alone, never send the all-ready
			// broadcast, and strand that client on the ready screen.
			int otherPlayers = MultiplayerSession.ConnectedPlayers.Count
				- (MultiplayerSession.ConnectedPlayers.ContainsKey(MultiplayerSession.HostUserID) ? 1 : 0);

			// Log the inputs, not just that a refresh happened - a load window that wrongly
			// opened the gate otherwise looks exactly like one that held.
			DebugConsole.Log(
				$"[ReadyManager] Refreshing ready state... (roster: {MultiplayerSession.ConnectedPlayers.Count}, " +
				$"others: {otherPlayers}, pending loads: {pendingLoads}, " +
				$"gate: {(canResume ? "OPEN" : "CLOSED")})");

			// The world was frozen for the ready screen; hand it back now the gate has opened.
			if (canResume)
				Utils.ResumeSimAfterReadyScreen();

			if (canResume && otherPlayers == 0)
			{
				AllClientsReadyPacket.ProcessAllReady();//bypass sending packet if its just the host left
				return;
			}

			if (canResume)
			{
				ReadyManager.SendAllReadyPacket();
			}
			else
			{
				ReadyManager.SendStatusUpdatePacketToClients();
			}
		}
	}
}
