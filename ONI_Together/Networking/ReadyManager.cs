using KSerialization;
using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport.Steamworks;
using Steamworks;
using System;
using System.Collections.Generic;
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
		/// HOST - shared "a client (re)connected" resync, invoked from both transports'
		/// connect callbacks (which run on the main thread): freeze the world for the ready
		/// screen and rebroadcast roster/ready state (show/hide + text) to everyone. The
		/// caller is responsible for marking the (re)connecting player Unready first.
		///
		/// <paramref name="isHostLoopback"/> is true for the LAN host's own local client
		/// connecting to its own server on start; in that case we skip the sim-pause (there
		/// is no remote player to wait on) but still refresh the roster. The flag is required
		/// (no default) so every transport must consciously decide whether its connection can
		/// be a host loopback rather than silently inheriting the "remote" behaviour.
		/// </summary>
		public static void HandleClientConnected(bool isHostLoopback)
		{
			using var _ = Profiler.Scope();

			// A remote joining client must not leave the rest of the table running while it
			// loads: pause the sim (broadcast to all peers) so the ready screen freezes the
			// world. The host's own loopback connect must not pause the sim on host start.
			if (!isHostLoopback)
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
		}

		public static void MarkAllAsUnready()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;

			if (MultiplayerSession.ConnectedPlayers.TryGetValue(MultiplayerSession.HostUserID, out var host))
				host.readyState = ClientReadyState.Ready; // Host is always ready

			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
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

			if (!MultiplayerSession.InSession)
				return;

			string text = GetScreenText();
			MultiplayerOverlay.Show(text);
		}

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
			return message;
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

			if (!MultiplayerSession.InSession)
				return true;

			// A client that disconnected to load the level can be removed from the live
			// roster (Riptide reconnects it under a new id) — IsEveryoneReady would then
			// stop seeing it and the gate would wrongly open mid-load. Keep gated while any
			// load is in flight. (Steamworks keeps a Connection==null placeholder instead,
			// so it reports no pending loads and relies on IsEveryoneReady below.)
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

			if (!MultiplayerSession.InSession)
				return;

			DebugConsole.Log("Refreshing ready state...");

			// A client mid load-reconnect has dropped off the roster (Riptide) but is not
			// gone. Don't take the "only host left -> all ready" shortcut and don't let the
			// all-ready close fire while a load is in flight, or the ready screen would
			// vanish (and the gate open) before the client finishes loading.
			bool loadingPending = NetworkConfig.TransportServer?.HasPendingLoadingClients == true;

			if (!loadingPending && MultiplayerSession.ConnectedPlayers.Count <= 1)
			{
				AllClientsReadyPacket.ProcessAllReady();//bypass sending packet if its just the host left
				return;
			}

			bool allReady = !loadingPending && ReadyManager.IsEveryoneReady();
			if (allReady)
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
