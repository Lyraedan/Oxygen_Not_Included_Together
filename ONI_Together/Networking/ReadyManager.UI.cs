using KSerialization;
using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.States;
using Shared.Profiling;
using Steamworks;

namespace ONI_Together.Networking
{
	public partial class ReadyManager
	{
		public static void RefreshScreen()
		{
			using var _ = Profiler.Scope();
			if (!MultiplayerSession.InSession)
				return;

			MultiplayerOverlay.Show(GetScreenText());
		}

		private static string GetScreenText()
		{
			using var _ = Profiler.Scope();
			int readyCount = GetReadyCount();
			int maxPlayers = MultiplayerSession.ConnectedPlayers.Values.Count;
			string message = string.Format(
				STRINGS.UI.MP_OVERLAY.SYNC.WAITING_FOR_PLAYERS_SYNC,
				readyCount,
				maxPlayers);
			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
				message += $"{player.PlayerName}: {GetReadyText(player.readyState)}\n";
			return message;
		}

		private static int GetReadyCount()
		{
			using var _ = Profiler.Scope();
			int count = 0;
			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (player.readyState.Equals(ClientReadyState.Ready))
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
			if (MultiplayerSession.IsHost && MultiplayerOverlay.IsOpen)
				RefreshScreen();
		}

		public static bool IsEveryoneReady()
		{
			using var _ = Profiler.Scope();
			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (player.PlayerId != MultiplayerSession.HostUserID && player.Connection == null)
					continue;
				if (!SyncBarrier.IsExactReady(player.readyState))
					return false;
			}
			return true;
		}

		internal static bool IsConnectedRemoteClient(
			ulong playerId,
			ulong hostUserId,
			bool hasConnection)
			=> playerId != hostUserId && hasConnection;

		private static bool HasConnectedRemoteClient()
		{
			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (IsConnectedRemoteClient(
					    player.PlayerId,
					    MultiplayerSession.HostUserID,
					    player.Connection != null))
					return true;
			}
			return false;
		}

		internal static bool CanSignalAllReady(
			bool barrierActive,
			bool hasConnectedRemote,
			bool everyoneReady)
			=> !barrierActive && (!hasConnectedRemote || everyoneReady);

		internal static void RefreshReadyState()
		{
			using var _ = Profiler.Scope();
			PruneSyncBarrier();
			if (!MultiplayerSession.InSession)
				return;

			DebugConsole.Log("Refreshing ready state...");
			bool hasConnectedRemote = HasConnectedRemoteClient();
			bool everyoneReady = !hasConnectedRemote || IsEveryoneReady();
			if (!CanSignalAllReady(_syncBarrier.IsActive, hasConnectedRemote, everyoneReady))
			{
				SendStatusUpdatePacketToClients();
				return;
			}

			if (!hasConnectedRemote)
			{
				AllClientsReadyPacket.ProcessAllReady();
				return;
			}
			SendAllReadyPacket();
		}
	}
}
