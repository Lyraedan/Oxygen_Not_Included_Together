using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.World;
using System.Collections;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking
{
	public static class GameServerHardSync
	{
		public static bool hardSyncDoneThisCycle = false;
		private static bool hardSyncInProgress = false;
		private static int numberOfClientsAtTimeOfSync = 0;

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

		public static void PerformHardSync(bool consumeDailyUse = false)
		{
			using var _ = Profiler.Scope();

			if (hardSyncInProgress)
			{
				DebugConsole.Log("[HardSync] A hard sync is already in progress.");
				return;
			}

			// A closed resume gate means a client is mid-join or mid-load. A hard sync fired
			// into that window starts a SECOND save transfer to a client already downloading
			// one and reloads it twice - measured in a Steam session, where the cycle-start
			// auto sync (GameClockPatch queues it on a 5s realtime delay, so it lands even
			// after the join paused the sim) hit 8s into a join. hardSyncDoneThisCycle is
			// deliberately left false: skipping is a postponement, not this cycle's sync.
			if (MultiplayerSession.IsHost && !ReadyManager.CanHostResume())
			{
				DebugConsole.LogWarning(
					"[HardSync] Skipped: a client is still joining or loading (resume gate closed)");
				return;
			}

			// Through the ready-screen bookkeeping, not a bare Pause: this pause has no
			// matching Unpause of its own anywhere (the automatic one in ProcessAllReady is
			// commented out upstream), so it leaked one pause level per hard sync and left
			// the host hammering the pause button afterwards. Flagged this way,
			// ResumeSimAfterReadyScreen reverses it when the gate reopens.
			Misc.Utils.PauseSimForReadyScreen();
			MultiplayerOverlay.Show(STRINGS.UI.MP_OVERLAY.SYNC.HARDSYNC_INPROGRESS);

            numberOfClientsAtTimeOfSync = MultiplayerSession.ConnectedPlayers.Count;
			var packet = new HardSyncPacket();
			PacketSender.SendToAllClients(packet);

			// Hide other player cursors as they are in hard sync and it'll reappear when they start sending packets again
			foreach (PlayerCursor cursor in MultiplayerSession.PlayerCursors.Values)
			{
				cursor.SetVisibility(false);
			}

			DebugConsole.Log($"[HardSync] Starting hard sync for {numberOfClientsAtTimeOfSync} client(s)...");
			CoroutineRunner.RunOne(HardSyncCoroutine(consumeDailyUse));
		}

		private static IEnumerator HardSyncCoroutine(bool consumeDailyUse = false)
		{
			using var _ = Profiler.Scope();

			hardSyncInProgress = true;

            ReadyManager.MarkAllAsUnready();
            SaveFileRequestPacket.SendSaveFileToAll();
            ReadyManager.RefreshScreen(); // Bring up ready screen for host

            int fileSize = SaveHelper.GetWorldSave().Length;
			int chunkSize = SaveHelper.SAVEFILE_CHUNKSIZE_KB * 1024;
			int chunkCount = Mathf.CeilToInt(fileSize / (float)chunkSize);
			float estimatedTransferDuration = chunkCount * SaveFileRequestPacket.SAVE_DATA_SEND_DELAY;
			yield return new WaitForSecondsRealtime(estimatedTransferDuration * numberOfClientsAtTimeOfSync);

			hardSyncDoneThisCycle = consumeDailyUse;
            hardSyncInProgress = false;
			// With the ready state I do not think this is needed anymore
			//SpeedControlScreen.Instance?.Unpause(false);
			//MultiplayerOverlay.Close();
		}
	}
}
