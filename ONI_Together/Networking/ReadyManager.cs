using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport.Lan;
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

			if (!MultiplayerSession.InActiveSession)
				return;

			string text = GetScreenText();
			MultiplayerOverlay.Show(text);
		}

		private static string GetScreenText()
		{
			using var _ = Profiler.Scope();

			var pendingLanNames = GetPendingLanLoadingNames();
			int readyCount = GetReadyCount();
			int maxPlayers = MultiplayerSession.ConnectedPlayers.Values.Count + pendingLanNames.Count;
			string message = string.Format(STRINGS.UI.MP_OVERLAY.SYNC.WAITING_FOR_PLAYERS_SYNC, readyCount, maxPlayers);
			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				message += $"{player.PlayerName}: {GetReadyText(player.readyState)}\n";
			}
			foreach (string loadingName in pendingLanNames)
			{
				message += $"{loadingName}: {GetReadyText(ClientReadyState.Loading)}\n";
			}
			return message;
		}

		private static System.Collections.Generic.List<string> GetPendingLanLoadingNames()
		{
			if (NetworkConfig.TransportServer is LiteNetLibServer lnlServer)
				return lnlServer.GetPendingLoadingClientNames();
			return new System.Collections.Generic.List<string>();
		}

		private static bool HasPendingLanLoadingClients()
		{
			return NetworkConfig.TransportServer is LiteNetLibServer lnlServer && lnlServer.HasPendingLoadingClients();
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
				case ClientReadyState.Loading:
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
		/// HOST ONLY - Check if all connected clients are ready
		/// </summary>
		/// <returns></returns>
		public static bool IsEveryoneReady()
		{
			using var _ = Profiler.Scope();

			if (HasPendingLanLoadingClients())
				return false;

			bool result = true;
			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (player.readyState != ClientReadyState.Ready)
				{
					result = false;

					break;
				}
			}
			return result;
		}

		internal static void RefreshReadyState()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession)
				return;

			if (MultiplayerSession.IsQuitting)
				return;

			DebugConsole.Log("Refreshing ready state...");
			if (MultiplayerSession.ConnectedPlayers.Count <= 1 && !HasPendingLanLoadingClients())
			{
				AllClientsReadyPacket.ProcessAllReady();//bypass sending packet if its just the host left
				return;
			}

			bool allReady = ReadyManager.IsEveryoneReady();
			if (allReady)
			{
				ReadyManager.SendAllReadyPacket();
			}
			else
			{
				if (MultiplayerSession.IsHost && MultiplayerOverlay.IsOpen)
					ReadyManager.RefreshScreen();

				// Broadcast updated overlay message to all clients
				ReadyManager.SendStatusUpdatePacketToClients();
			}
		}
	}
}
