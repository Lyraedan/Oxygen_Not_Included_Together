using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Misc;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport.Steamworks;
using Steamworks;
using Shared.Profiling;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ONI_Together.Networking
{
	public class ReadyManager
	{
		private const float HEARTBEAT_STALE_AFTER = 15f;
		private const float STALE_CHECK_INTERVAL = 5f;

		private static readonly Dictionary<ulong, float> _lastHeartbeat = new();
		private static readonly Dictionary<ulong, float> _loadingClients = new();
		private static float _lastStaleCheckTime;
		private static bool _wasAllReady;

		public static void SetupListeners()
		{
			using var _ = Profiler.Scope();

			SteamLobby.OnLobbyMembersRefreshed += UpdateReadyStateTracking;
		}

		/// <summary>
		/// HOST ONLY - Records that a client is alive. Returns true if this is the first
		/// heartbeat seen from this client since the last ready-gate cycle (used to force
		/// a status push so freshly-rejoined clients receive the current ready list).
		/// </summary>
		public static bool RegisterHeartbeat(ulong senderId)
		{
			using var _ = Profiler.Scope();

			float now = Time.unscaledTime;
			bool isFirst = !_lastHeartbeat.ContainsKey(senderId);
			_lastHeartbeat[senderId] = now;
			return isFirst;
		}

		/// <summary>
		/// HOST ONLY - Marks a client as loading (disconnected to load the save, will reconnect).
		/// Keeps the ready gate open and silences stale-heartbeat warnings while it loads.
		/// </summary>
		public static void MarkClientLoading(ulong senderId)
		{
			using var _ = Profiler.Scope();

			_loadingClients[senderId] = Time.unscaledTime;
			NetworkConfig.TransportServer.MarkClientLoading(senderId);
		}

		/// <summary>
		/// HOST ONLY - Clears the loading mark once a client reconnects and reports a state.
		/// </summary>
		public static void CompleteLoading(ulong senderId)
		{
			using var _ = Profiler.Scope();

			_loadingClients.Remove(senderId);
			NetworkConfig.TransportServer.ConsumeReconnectFromLoad(senderId);
		}

		/// <summary>
		/// HOST ONLY - Updates the cached player identity (name) and, on LAN, announces the
		/// player to all clients and the chat so join messages stay in sync.
		/// </summary>
		public static void ReportPlayerIdentity(MultiplayerPlayer player, string playerName, bool isFirstSeen)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;

			if (!string.IsNullOrEmpty(playerName))
				MultiplayerSession.KnownPlayerNames[player.PlayerId] = playerName;

			bool nameChanged = !string.IsNullOrEmpty(playerName) && player.PlayerName != playerName;
			if (nameChanged)
				player.PlayerName = playerName;

			if (!NetworkConfig.IsLanConfig())
				return;
			if (!nameChanged && !isFirstSeen)
				return;

			bool isLoadingReconnect = NetworkConfig.TransportServer.ConsumeReconnectFromLoad(player.PlayerId);

			if (!isLoadingReconnect)
			{
				OxySyncChat.AddSystemMessage(
					string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_JOINED, player.PlayerName));
			}

			PacketSender.SendToAllClients(new ClientReadyStatusPacket
			{
				SenderId = MultiplayerSession.HostUserID,
				PlayerName = Utils.GetLocalPlayerName()
			});

			PacketSender.SendToAllClients(new ClientReadyStatusPacket
			{
				SenderId = player.PlayerId,
				PlayerName = player.PlayerName
			});
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

			_wasAllReady = false;
			_loadingClients.Clear();
			_lastHeartbeat.Clear();
			PushReadyStatus();
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

			string text = BuildStatusText();
			MultiplayerOverlay.Show(text);
		}

		public static string BuildStatusText()
		{
			using var _ = Profiler.Scope();

			int readyCount = GetReadyCount();
			int maxPlayers = MultiplayerSession.ConnectedPlayers.Values.Count;
			string message = string.Format(STRINGS.UI.MP_OVERLAY.SYNC.WAITING_FOR_PLAYERS_SYNC, readyCount, maxPlayers);
			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				message += $"{player.PlayerName}: {GetReadyText(player.readyState)}\n";
			}
			return message;
		}

		/// <summary>
		/// HOST ONLY - Pushes the current aggregated ready status to all clients via the
		/// ReadyStateSyncer SyncVar, and refreshes the host's own overlay.
		/// </summary>
		private static void PushReadyStatus()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;

			ReadyStateSyncer.Instance?.PushReadyStatusText(BuildStatusText());
			RefreshScreen();
		}

		private static int GetReadyCount()
		{
			using var _ = Profiler.Scope();

			int count = 0;
			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (player.readyState.Equals(ClientReadyState.Ready))
				{
					count++;
				}
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
		/// HOST ONLY - Check if all connected clients are ready. Loading clients are
		/// treated as pending and block the gate.
		/// </summary>
		public static bool IsEveryoneReady()
		{
			using var _ = Profiler.Scope();

			if (_loadingClients.Count > 0)
				return false;

			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (player.readyState == ClientReadyState.Unready)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// HOST ONLY - Re-evaluates the ready gate. Fires the all-ready signal exactly once
		/// on the rising edge, otherwise pushes the current status text to all clients.
		/// </summary>
		internal static void RefreshReadyState()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession)
				return;

			if (MultiplayerSession.IsQuitting)
				return;

			bool anyLoading = _loadingClients.Count > 0;
			bool allReady = MultiplayerSession.ConnectedPlayers.Count > 0
				&& !anyLoading
				&& IsEveryoneReady();

			if (allReady)
			{
				if (_wasAllReady)
					return;

				_wasAllReady = true;
				DebugConsole.Log("Refreshing ready state...");
				DebugConsole.Log("All players are ready! Broadcasting all-ready signal");
				ReadyStateSyncer.Instance?.BroadcastAllReady();
				AllClientsReadyPacket.ProcessAllReady();
			}
			else
			{
				_wasAllReady = false;
				PushReadyStatus();
			}
		}

		/// <summary>
		/// HOST ONLY - Logs clients whose heartbeat has gone stale and evicts loading entries
		/// that never reconnected. No forced disconnects - the transport timeout handles dead
		/// clients.
		/// </summary>
		internal static void CheckForStaleHeartbeats()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;

			float now = Time.unscaledTime;
			if (now - _lastStaleCheckTime < STALE_CHECK_INTERVAL)
				return;
			_lastStaleCheckTime = now;

			bool loadingEvicted = false;
			float loadingTimeout = ONI_Together.Configuration.Instance.Host.TimeoutSeconds;
			foreach (var kvp in _loadingClients.ToList())
			{
				if (now - kvp.Value > loadingTimeout)
				{
					_loadingClients.Remove(kvp.Key);
					loadingEvicted = true;
					DebugConsole.LogWarning($"[ReadyManager] Loading client {kvp.Key} never reconnected after {loadingTimeout}s - treating as left");
				}
			}
			if (loadingEvicted)
				RefreshReadyState();

			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (player.PlayerId == MultiplayerSession.HostUserID)
					continue;
				if (player.Connection == null)
					continue;
				if (_loadingClients.ContainsKey(player.PlayerId))
					continue;
				if (!_lastHeartbeat.TryGetValue(player.PlayerId, out float last))
					continue;
				if (now - last <= HEARTBEAT_STALE_AFTER)
					continue;

				DebugConsole.LogWarning($"[ReadyManager] Player {player.PlayerName} ({player.PlayerId}) heartbeat stale for {now - last:F1}s - relying on transport timeout");
			}
		}
	}
}