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
		/// HOST - shared "a client (re)connected" resync, invoked from every transport's
		/// connect callback (which run on the main thread): freeze the world for the ready
		/// screen and rebroadcast roster/ready state (show/hide + text) to everyone. The
		/// caller is responsible for marking the (re)connecting player Unready first.
		/// </summary>
		public static void HandleClientConnected()
		{
			using var _ = Profiler.Scope();

			// A joining client must not leave the rest of the table running while it loads:
			// pause the sim (broadcast to all peers) so the ready screen freezes the world.
			// The host's own loopback connect on LAN host-start happens before the session is
			// established, and PauseSimForReadyScreen's own InActiveSession guard drops it.
			Utils.PauseSimForReadyScreen();

			// Host owns the roster/visibility: recompute and rebroadcast show/hide + text.
			RefreshScreen();
			RefreshReadyState();
		}

		public static void SendAllReadyPacket()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;

			//CoroutineRunner.RunOne(DelayAllReadyBroadcast());
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

			// The Loading notice is what arms the host's load-window gate, and it is sent
			// moments before this client tears its connection down. Log the send so a lost
			// one can be told apart from one that was never sent.
			if (state == ClientReadyState.Loading)
				DebugConsole.Log($"[ReadyManager] Sent Loading notice to host as {packet.SenderId}");
		}

		public static void MarkAllAsUnready()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;

			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				// The host's stored readyState is never read - IsConsideredReady short-circuits
				// on the host id before touching the field - so leave it untouched.
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
		/// The whole overlay body, suffix included, for both the host's own screen and the
		/// copy shipped to clients in <see cref="ClientReadyStatusUpdatePacket"/> - the two
		/// sides must read the same, so only one place builds it.
		/// </summary>
		private static string GetScreenText()
		{
			using var _ = Profiler.Scope();

			int readyCount = GetReadyCount();
			// A client mid load-reconnect is off the roster (Riptide) but still expected, so
			// add it to the total — otherwise the overlay reads e.g. "2/2" while we are
			// (correctly) still waiting on the loader.
			int pendingLoads = NetworkConfig.TransportServer?.PendingLoadingClientCount ?? 0;
			int maxPlayers = MultiplayerSession.ConnectedPlayers.Values.Count + pendingLoads;
			string message = string.Format(STRINGS.UI.MP_OVERLAY.SYNC.WAITING_FOR_PLAYERS_SYNC, readyCount, maxPlayers);
			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				// Show the same readiness the count/gate use (host always reads ready).
				ClientReadyState displayState = IsConsideredReady(player)
					? ClientReadyState.Ready
					: ClientReadyState.Unready;
				message += $"{player.PlayerName}: {GetReadyText(displayState)}\n";
			}

			// Say why the tools stopped responding. A refusal with no explanation is
			// indistinguishable from the game having frozen, and the player's next move is
			// alt-F4.
			return message + STRINGS.UI.MP_OVERLAY.SYNC.INPUT_LOCKED_WHILE_WAITING;
		}

		/// <summary>
		/// Single source of truth for "is this player ready" used by the overlay text, the
		/// ready count and the resume gate. The host is always considered ready regardless
		/// of its stored flag.
		///
		/// NOTE: a disconnected client (Connection == null) is deliberately NOT skipped /
		/// treated as ready. Clients drop their socket precisely *while loading the level*,
		/// and the host must stay gated through that window — Connection == null cannot tell
		/// "loading" apart from "crashed". A client that has truly left is removed from
		/// ConnectedPlayers by the transport / Steam-lobby leave handlers, which clears the
		/// gate; on a hard crash that removal is just delayed until lobby eviction.
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
		/// The authority gate for resuming the sim. The host may only resume/unpause
		/// when every connected player is ready. Outside a session there is nothing to
		/// gate. This is the real safety — UI visibility must never permit resume.
		/// </summary>
		public static bool CanHostResume()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession)
				return true;

			// Both LAN transports remove a client from ConnectedPlayers when it disconnects to
			// load the level, so IsEveryoneReady stops seeing it and the gate would wrongly
			// open mid-load. Keep gated while any load is in flight. (Steamworks instead keeps
			// a Connection==null placeholder in the roster, so it reports no pending loads and
			// is covered by IsEveryoneReady below.)
			if (NetworkConfig.TransportServer?.HasPendingLoadingClients == true)
				return false;

			return IsEveryoneReady();
		}

		/// <summary>
		/// HOST ONLY - Check if all connected clients are ready
		/// </summary>
		/// <returns></returns>
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

			// A client mid load-reconnect has dropped off the roster but is not gone. Don't
			// take the "only host left -> all ready" shortcut and don't let the all-ready
			// close fire while a load is in flight, or the ready screen would vanish (and the
			// gate open) before the client finishes loading.
			int pendingLoads = NetworkConfig.TransportServer?.PendingLoadingClientCount ?? 0;
			bool canResume = CanHostResume();

			// How many players are here besides us. Whether the host counts itself in
			// ConnectedPlayers differs per transport - LiteNetLibServer inserts the host as
			// ConnectedPlayers[1], Steamworks never adds it - so the raw roster size does not
			// answer "is anyone else here". Reading it as if it did meant that on Steam, with
			// exactly one client connected, the host took the "only me left" shortcut below,
			// resumed alone, and never sent the all-ready broadcast; the client was left on the
			// ready screen with no way off it. HostUserID is the host's own id on both
			// (measured: "Host set to: 1" on LAN, "Host set to: 76561198367584567" on Steam).
			int otherPlayers = MultiplayerSession.ConnectedPlayers.Count
				- (MultiplayerSession.ConnectedPlayers.ContainsKey(MultiplayerSession.HostUserID) ? 1 : 0);

			// Log the inputs, not just that a refresh happened. Every failure of this gate so
			// far has been invisible: the broken and the working path both printed a bare
			// "Refreshing ready state..." and nothing else, so a load window that wrongly
			// opened the gate looked exactly like one that held. "others" is separate from
			// "roster" because the difference between them is where the shortcut bug hid.
			DebugConsole.Log(
				$"[ReadyManager] Refreshing ready state... (roster: {MultiplayerSession.ConnectedPlayers.Count}, " +
				$"others: {otherPlayers}, pending loads: {pendingLoads}, " +
				$"gate: {(canResume ? "OPEN" : "CLOSED")})");

			// The world was frozen for the ready screen; hand it back now the gate has opened.
			// Without this the host was left in front of a stopped world after every join and
			// had to toggle the speed itself before the play button would do anything.
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
				// Broadcast updated overlay message to all clients
				ReadyManager.SendStatusUpdatePacketToClients();
			}
		}
	}
}
