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
		public static bool IsSynchronizing { get; private set; }
		internal static float GameplayDrainUntil { get; private set; }
		public static bool IsSimulationLocked => MultiplayerSession.IsHost
			? IsSynchronizing
			: GameClient.State == ClientState.LoadingWorld;

		internal static void ResetSynchronizationState()
		{
			if (IsSynchronizing)
			{
				foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
				{
					if (player.PlayerId == MultiplayerSession.HostUserID)
						continue;

					if (player.readyState == ClientReadyState.Loading)
						player.readyState = ClientReadyState.Unready;
				}
			}

			IsSynchronizing = false;
			GameplayDrainUntil = 0f;
			GameServerHardSync.ResetSynchronizationState();
		}

		public static bool BeginSynchronization()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost || IsSynchronizing)
				return false;

			IsSynchronizing = true;
			SpeedControlScreen.Instance?.Pause(false);

			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (player.PlayerId == MultiplayerSession.HostUserID)
				{
					player.readyState = ClientReadyState.Ready;
					continue;
				}
				if (player.Connection == null || !player.ProtocolVerified)
					continue;

				player.readyState = ClientReadyState.Loading;
			}

			RefreshScreen();
			SendStatusUpdatePacketToClients();
			return true;
		}

		internal static void StartGameplayDrain(float durationSeconds)
		{
			GameplayDrainUntil = UnityEngine.Time.realtimeSinceStartup + durationSeconds;
		}

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
			if (!IsSynchronizing || !IsEveryoneReady())
				return;

			PacketSender.SendToAllClients(new AllClientsReadyPacket(), PacketSendMode.Reliable);
			IsSynchronizing = false;
			GameplayDrainUntil = 0f;
			GameServerHardSync.OnSynchronizationCompleted();
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

		public static void SetPlayerReadyState(MultiplayerPlayer player, ClientReadyState state)
		{
			using var _ = Profiler.Scope();

			if (player.PlayerId == MultiplayerSession.HostUserID)
				return;
			if (state == ClientReadyState.Ready
				&& (!IsSynchronizing || player.readyState != ClientReadyState.Loading))
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

			int readyCount = GetReadyCount();
			int maxPlayers = MultiplayerSession.ConnectedPlayers.Count;
			string message = string.Format(STRINGS.UI.MP_OVERLAY.SYNC.WAITING_FOR_PLAYERS_SYNC, readyCount, maxPlayers);
			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				message += $"{player.PlayerName}: {GetReadyText(player.readyState)}\n";
			}
			return message;
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
				case ClientReadyState.Loading:
					return global::STRINGS.UI.FRONTEND.LOADING;
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

			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (player.readyState != ClientReadyState.Ready)
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

			DebugConsole.Log("Refreshing ready state...");
			bool allReady = ReadyManager.IsEveryoneReady();
			SendStatusUpdatePacketToClients();
			if (allReady && IsSynchronizing)
			{
				ReadyManager.SendAllReadyPacket();
			}
		}
	}
}
