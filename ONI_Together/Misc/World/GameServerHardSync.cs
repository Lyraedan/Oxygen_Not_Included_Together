using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;

namespace ONI_Together.Networking
{
	public static class GameServerHardSync
	{
		public static bool hardSyncDoneThisCycle = false;
		private static bool _consumeDailyUse;
		public static bool IsHardSyncInProgress => ReadyManager.IsSynchronizing;

		public static void PerformHardSync(bool consumeDailyUse = false)
		{
			using var _ = Profiler.Scope();

			if (!ReadyManager.BeginSynchronization())
			{
				DebugConsole.Log("[HardSync] Synchronization is already in progress.");
				return;
			}

			_consumeDailyUse = consumeDailyUse;
			hardSyncDoneThisCycle = false;
			int clientCount = 0;
			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (player.PlayerId == MultiplayerSession.HostUserID)
					continue;

				if (player.Connection == null)
					continue;

				clientCount++;
				PacketSender.SendToPlayer(player.PlayerId, new HardSyncPacket());
			}

			foreach (PlayerCursor cursor in MultiplayerSession.PlayerCursors.Values)
				cursor.SetVisibility(false);

			DebugConsole.Log($"[HardSync] Synchronization started for {clientCount} client(s).");
			SaveFileRequestPacket.SendSaveFileToAll();
			ReadyManager.RefreshReadyState();
		}

		internal static void OnSynchronizationCompleted()
		{
			if (_consumeDailyUse)
				hardSyncDoneThisCycle = true;

			_consumeDailyUse = false;
		}
	}
}
