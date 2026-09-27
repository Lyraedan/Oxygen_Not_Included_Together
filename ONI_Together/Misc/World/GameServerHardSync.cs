using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.States;
using System.Collections;
using System.Collections.Generic;
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

			SpeedControlScreen.Instance?.Pause(false); // Pause the game
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
			int generation = ++_generation;
			_unreadySince.Clear();
			CoroutineRunner.RunOne(HardSyncCoroutine(consumeDailyUse));
			CoroutineRunner.RunOne(ReadyWatchdog(generation));
		}

		/// <summary>How long a client may stay unready after a hard sync before the host stops waiting for it.</summary>
		public const float ReadyTimeoutSeconds = 120f;

		// Bumped by every hard sync; the watchdog of an older sync stops when it sees a newer one.
		private static int _generation;

		// Keyed by the player object, not the id: ids are reused after a reconnect.
		private static readonly Dictionary<MultiplayerPlayer, float> _unreadySince = new();

		/// <summary>
		/// Marks a client ready once it has been unready for ReadyTimeoutSeconds, so one stuck client
		/// no longer keeps the host on the ready screen. Runs until the next hard sync.
		/// </summary>
		private static IEnumerator ReadyWatchdog(int generation)
		{
			while (true)
			{
				yield return new WaitForSecondsRealtime(2f);
				if (_generation != generation || !MultiplayerSession.IsHost || !MultiplayerSession.InActiveSession)
					yield break;

				float now = Time.unscaledTime;
				bool released = false;
				foreach (var player in MultiplayerSession.ConnectedPlayers.Values)
				{
					if (player.PlayerId == MultiplayerSession.HostUserID) continue;
					if (player.readyState == ClientReadyState.Ready)
					{
						_unreadySince.Remove(player);
						continue;
					}
					if (!_unreadySince.TryGetValue(player, out float since))
					{
						_unreadySince[player] = now;
						continue;
					}
					if (now - since < ReadyTimeoutSeconds) continue;

					DebugConsole.LogWarning($"[HardSync] {player.PlayerName} ({player.PlayerId}) did not report ready within {ReadyTimeoutSeconds:F0} s; continuing without them");
					ReadyManager.SetPlayerReadyState(player, ClientReadyState.Ready);
					_unreadySince.Remove(player);
					released = true;
				}

				if (released)
					ReadyManager.RefreshReadyState();
			}
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
