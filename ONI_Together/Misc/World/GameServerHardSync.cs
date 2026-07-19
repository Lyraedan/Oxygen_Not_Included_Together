using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;

namespace ONI_Together.Networking
{
	public static class GameServerHardSync
	{
		public static bool hardSyncDoneThisCycle = false;
		private static bool hardSyncInProgress = false;
		private static bool consumeDailyUseOnCompletion;

		public static bool IsHardSyncInProgress
		{

			get
			{
				return hardSyncInProgress;
			}
			set
			{
				hardSyncInProgress = value;
			}
		}

		public static void ResetSessionState()
		{
			ProductionDesyncRecovery.ResetSessionState();
			hardSyncDoneThisCycle = false;
			hardSyncInProgress = false;
			consumeDailyUseOnCompletion = false;
		}

		public static void PerformHardSync(bool consumeDailyUse = false)
		{
			using var _ = Profiler.Scope();
			ProductionDesyncRecovery.CancelForHardSync();

			if (hardSyncInProgress)
			{
				DebugConsole.Log("[HardSync] A hard sync is already in progress.");
				return;
			}

			MultiplayerOverlay.Show(STRINGS.UI.MP_OVERLAY.SYNC.HARDSYNC_INPROGRESS);

			var clients = ReadyManager.GetReadyClientIds();
			PacketSender.SendToAllClients(new HardSyncPacket(), PacketSendMode.ReliableImmediate);

			// Hide other player cursors as they are in hard sync and it'll reappear when they start sending packets again
			foreach (PlayerCursor cursor in MultiplayerSession.PlayerCursors.Values)
			{
				cursor.SetVisibility(false);
			}

			hardSyncInProgress = true;
			consumeDailyUseOnCompletion = consumeDailyUse;
			foreach (ulong clientId in clients)
				ReadyManager.BeginSyncBarrier(clientId);
			SaveFileRequestPacket.SendSaveFileToAll(clients);
			ReadyManager.RefreshScreen();

			DebugConsole.Log($"[HardSync] Starting hard sync for {clients.Count} client(s)...");
			if (!ReadyManager.HasActiveSyncBarrier)
				OnSyncBarrierCompleted();
		}

		internal static void OnSyncBarrierCompleted()
		{
			if (!hardSyncInProgress)
				return;

			hardSyncDoneThisCycle = consumeDailyUseOnCompletion;
			hardSyncInProgress = false;
			DebugConsole.Log("[HardSync] All sync clients are ready.");
		}
	}
}
