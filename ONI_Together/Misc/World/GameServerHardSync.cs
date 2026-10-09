using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.States;
using System;
using System.Collections;
using Shared.Profiling;

namespace ONI_Together.Networking
{
	public static class GameServerHardSync
	{
		private const float ClientTrafficDrainSeconds = 0.15f;
		private static byte[] _synchronizationSnapshot;

		public static bool hardSyncDoneThisCycle = false;
		private static bool _consumeDailyUse;
		public static bool IsHardSyncInProgress => ReadyManager.IsSynchronizing;
		internal static byte[] SynchronizationSnapshot => _synchronizationSnapshot;

		public static void PerformHardSync(bool consumeDailyUse = false)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
			{
				DebugConsole.LogWarning("[HardSync] Only the host can start synchronization.");
				return;
			}

			if (ReadyManager.IsSynchronizing)
			{
				DebugConsole.Log("[HardSync] Synchronization is already in progress.");
				return;
			}

			if (!ReadyManager.BeginSynchronization())
			{
				DebugConsole.LogWarning("[HardSync] Could not start synchronization.");
				return;
			}

			_consumeDailyUse = consumeDailyUse;
			hardSyncDoneThisCycle = false;
			int clientCount = 0;
			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (player.PlayerId == MultiplayerSession.HostUserID)
					continue;

				if (player.Connection == null || !player.ProtocolVerified)
					continue;

				if (player.readyState != ClientReadyState.Loading)
					continue;

				clientCount++;
				PacketSender.SendToPlayer(player.PlayerId, new HardSyncPacket());
			}

			foreach (PlayerCursor cursor in MultiplayerSession.PlayerCursors.Values)
				cursor.SetVisibility(false);

			ReadyManager.StartGameplayDrain(ClientTrafficDrainSeconds);
			DebugConsole.Log($"[HardSync] Synchronization started for {clientCount} client(s).");
			CoroutineRunner.RunOne(DrainClientTrafficThenTransfer());
			ReadyManager.RefreshReadyState();
		}

		private static IEnumerator DrainClientTrafficThenTransfer()
		{
			while (ReadyManager.IsSynchronizing
				&& UnityEngine.Time.realtimeSinceStartup < ReadyManager.GameplayDrainUntil)
				yield return null;

			if (!ReadyManager.IsSynchronizing)
				yield break;

			// Close gameplay admission before yielding so admitted dispatches finish before capture.
			yield return null;
			if (!ReadyManager.IsSynchronizing)
				yield break;

			try
			{
				_synchronizationSnapshot = SaveHelper.GetWorldSave();
				SaveFileRequestPacket.SendSaveFileToAll(_synchronizationSnapshot);
				ReadyManager.RefreshReadyState();
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[HardSync] Failed to capture or start the synchronization snapshot transfer: {ex}");
				ReadyManager.ResetSynchronizationState();
			}
		}

		internal static void OnSynchronizationCompleted()
		{
			if (_consumeDailyUse)
				hardSyncDoneThisCycle = true;

			_consumeDailyUse = false;
			_synchronizationSnapshot = null;
		}

		internal static void ResetSynchronizationState()
		{
			_consumeDailyUse = false;
			SaveFileRequestPacket.ClearPendingSynchronizationTransfers();
			_synchronizationSnapshot = null;
		}
	}
}
