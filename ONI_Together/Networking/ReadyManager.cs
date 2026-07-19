using KSerialization;
using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport.Steamworks;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Threading;
using Shared.Profiling;

namespace ONI_Together.Networking
{
	public partial class ReadyManager
	{
		private static readonly SyncBarrier _syncBarrier = new();
		internal const int LoadingLeaseSeconds = 300;
		private static long _nextSnapshotGeneration;
		private static ulong _reconnectToken;
		private static long _clientSnapshotGeneration;
		private static ulong _pendingLoadingToken;
		private static System.Action _pendingWorldLoad;
		private static float _nextBarrierPruneAt;
		private static bool _ownsAutomaticPause;
		internal static bool HasActiveSyncBarrier => _syncBarrier.IsActive;
		internal static ulong ReconnectToken => _reconnectToken;
		internal static long ClientSnapshotGeneration => _clientSnapshotGeneration;

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

			List<ulong> readyClientIds = GetReadyClientIds();
			foreach (ulong clientId in readyClientIds)
			{
				if (!PacketSender.SendToPlayer(
					    clientId,
					    new AllClientsReadyPacket(),
					    PacketSendMode.ReliableImmediate))
				{
					DebugConsole.LogWarning(
						$"[ReadyManager] Failed to confirm Ready for {clientId}");
				}
			}
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

		public static bool SendReadyStatusPacket(ClientReadyState state)
		{
			using var _ = Profiler.Scope();

			// Host is always considered ready so it doesn't send these
			if (MultiplayerSession.IsHost)
				return false;

			if (state == ClientReadyState.Loading && _reconnectToken == 0)
				_reconnectToken = CreateReconnectToken();

			var packet = new ClientReadyStatusPacket
			{
				SenderId = NetworkConfig.GetLocalID(),
				Status = state,
				PlayerName = Utils.GetLocalPlayerName(),
				ReconnectToken = _reconnectToken,
				SnapshotGeneration = _clientSnapshotGeneration
			};
			return PacketSender.SendToHost(packet);
		}

		private static ulong CreateReconnectToken()
		{
			byte[] bytes = Guid.NewGuid().ToByteArray();
			ulong token = BitConverter.ToUInt64(bytes, 0);
			return token == 0 ? 1UL : token;
		}

		internal static bool RequestLoadingApproval(long snapshotGeneration, System.Action loadWorld)
		{
			if (MultiplayerSession.IsHost || loadWorld == null || snapshotGeneration <= 0
			    || snapshotGeneration != _clientSnapshotGeneration || _pendingWorldLoad != null)
				return false;

			_reconnectToken = CreateReconnectToken();
			_pendingLoadingToken = _reconnectToken;
			_pendingWorldLoad = loadWorld;
			if (SendReadyStatusPacket(ClientReadyState.Loading))
			{
				GameClient.BeginLoadingApprovalWait(_reconnectToken, snapshotGeneration);
				return true;
			}

			CancelPendingClientWorldLoad();
			return false;
		}

		internal static bool AcceptLoadingApproval(ulong reconnectToken, long snapshotGeneration)
		{
			if (reconnectToken == 0 || reconnectToken != _pendingLoadingToken
			    || snapshotGeneration <= 0 || snapshotGeneration != _clientSnapshotGeneration
			    || _pendingWorldLoad == null)
				return false;

			System.Action loadWorld = _pendingWorldLoad;
			_pendingWorldLoad = null;
			_pendingLoadingToken = 0;
			if (!TryRunApprovedWorldLoad(loadWorld, exception =>
			    {
				    DebugConsole.LogError(
					    $"[ReadyManager] Approved world load failed: {exception}", false);
				    GameClient.FailWorldLoadHandshake(
					    "The synchronized world could not be loaded.");
			    }))
			{
				return false;
			}
			GameClient.EndLoadingApprovalWait(reconnectToken, snapshotGeneration);
			return true;
		}

		internal static bool TryRunApprovedWorldLoad(
			System.Action loadWorld, System.Action<System.Exception> onFailure)
		{
			try
			{
				loadWorld();
				return true;
			}
			catch (System.Exception exception)
			{
				onFailure?.Invoke(exception);
				return false;
			}
		}

		internal static bool TryBeginClientSnapshot(long snapshotGeneration)
		{
			if (snapshotGeneration <= 0 || snapshotGeneration < _clientSnapshotGeneration)
				return false;
			if (snapshotGeneration == _clientSnapshotGeneration)
				return true;

			_clientSnapshotGeneration = snapshotGeneration;
			_reconnectToken = 0;
			_pendingLoadingToken = 0;
			_pendingWorldLoad = null;
			return true;
		}

		internal static bool IsCurrentClientSnapshot(long snapshotGeneration)
			=> snapshotGeneration > 0 && snapshotGeneration == _clientSnapshotGeneration;

		internal static bool IsExactReadyAcceptance(
			ulong localToken,
			long localGeneration,
			ulong acknowledgedToken,
			long acknowledgedGeneration)
			=> localToken != 0 && localGeneration > 0
			   && acknowledgedToken == localToken
			   && acknowledgedGeneration == localGeneration;

		internal static bool TryConfirmReadyAccepted(ulong reconnectToken, long snapshotGeneration)
		{
			if (!IsExactReadyAcceptance(
				    _reconnectToken,
				    _clientSnapshotGeneration,
				    reconnectToken,
				    snapshotGeneration))
				return false;

			ClearReconnectProof();
			return true;
		}

		internal static void ClearReconnectProof()
		{
			_reconnectToken = 0;
			_pendingLoadingToken = 0;
			_pendingWorldLoad = null;
			GameClient.CancelWorldLoadPhase();
		}

		public static bool SetPlayerReadyState(
			MultiplayerPlayer player,
			ClientReadyState state,
			ulong reconnectToken,
			long snapshotGeneration)
		{
			using var _ = Profiler.Scope();

			if (player == null || player.PlayerId == MultiplayerSession.HostUserID)
				return false;

			if (state == ClientReadyState.Loading)
			{
				if (!_syncBarrier.TryAcceptLoading(player.PlayerId, reconnectToken, snapshotGeneration))
					return false;
				player.readyState = state;
				return true;
			}

			if (SyncBarrier.IsExactReady(state))
				return CommitReadyOrResend(player, reconnectToken, snapshotGeneration);

			player.readyState = state;
			return state == ClientReadyState.Unready;
		}

		internal static bool BeginSyncBarrier(ulong clientId)
		{
			if (!MultiplayerSession.ConnectedPlayers.TryGetValue(clientId, out var player)
				|| clientId == MultiplayerSession.HostUserID
				|| player.Connection == null
				|| !player.ProtocolVerified)
			{
				return false;
			}

			if (_syncBarrier.Contains(clientId))
				return true;

			var speedControl = SpeedControlScreen.Instance;
			if (!_syncBarrier.Add(
				    clientId,
				    speedControl?.IsPaused ?? false,
				    pauseOwnedBySession: _ownsAutomaticPause))
				return false;
			ReliableSyncBacklog.Begin(clientId);
			player.readyState = ClientReadyState.Unready;
			if (speedControl != null && !speedControl.IsPaused)
			{
				SetPauseWithoutLocalPatch(speedControl, paused: true);
				_ownsAutomaticPause = true;
			}
			if (speedControl != null)
				SpeedChangePacket.SubmitLocalChange(SpeedChangePacket.SpeedState.Paused);

			return true;
		}

		internal static bool BeginSnapshotEpoch(ulong clientId, out long snapshotGeneration)
		{
			snapshotGeneration = 0;
			if (!_syncBarrier.Contains(clientId))
				return false;

			long generation = Interlocked.Increment(ref _nextSnapshotGeneration);
			if (generation <= 0)
			{
				Interlocked.Exchange(ref _nextSnapshotGeneration, 1);
				generation = 1;
			}
			if (!_syncBarrier.MarkTransferStarted(clientId, generation))
				return false;

			snapshotGeneration = generation;
			return true;
		}

		internal static bool TryBeginWorldBaseline(ulong clientId, long snapshotGeneration)
		{
			if (!MultiplayerSession.ConnectedPlayers.TryGetValue(clientId, out MultiplayerPlayer player)
			    || player.Connection == null
			    || !_syncBarrier.TryBeginWorldBaseline(clientId, snapshotGeneration))
			{
				return false;
			}

			// Everything before this cut is older than the absolute baseline. New
			// reliable deltas are journalled separately and replayed after Ready.
			ReliableSyncBacklog.Begin(clientId);
			return true;
		}

		internal static List<ulong> GetReadyClientIds()
		{
			var result = new List<ulong>();
			foreach (var player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (player.PlayerId != MultiplayerSession.HostUserID
					&& player.Connection != null
					&& player.ProtocolVerified
					&& SyncBarrier.IsExactReady(player.readyState))
				{
					result.Add(player.PlayerId);
				}
			}
			return result;
		}

		private static void CompleteSyncBarrier(ulong clientId)
		{
			bool completed = _syncBarrier.Complete(clientId);
			if (completed)
				ReliableSyncBacklog.Clear(clientId);
			FinishSyncBarrierIfNeeded(completed);
		}

		private static void PruneSyncBarrier()
		{
			var expiredClients = new List<ulong>();
			bool changed = _syncBarrier.Prune(id =>
					IsPendingClientStillExpected(id),
				TimeSpan.FromSeconds(LoadingLeaseSeconds),
				System.DateTime.UtcNow,
				expiredClients);
			foreach (ulong clientId in expiredClients)
			{
				DebugConsole.LogWarning($"[ReadyManager] Snapshot deadline expired for {clientId}; aborting transfer");
				SaveFileTransferManager.CancelTransfers(clientId);
				if (NetworkConfig.TransportServer is ONI_Together.Networking.Transport.Lan.RiptideServer server)
					server.TcpTransfer?.CancelTransfers(clientId);
				if (MultiplayerSession.ConnectedPlayers.TryGetValue(clientId, out MultiplayerPlayer player))
					player.CompleteSaveTransfer();
				ReliableSyncBacklog.Clear(clientId);
				NetworkConfig.TransportServer?.KickClient(clientId);
			}
			ReliableSyncBacklog.Prune(id => _syncBarrier.Contains(id));
			FinishSyncBarrierIfNeeded(changed);
		}

		internal static void Update()
		{
			PruneCompletedReadyProofs(System.DateTime.UtcNow);
			if (!_syncBarrier.IsActive || UnityEngine.Time.unscaledTime < _nextBarrierPruneAt)
				return;
			_nextBarrierPruneAt = UnityEngine.Time.unscaledTime + 1f;
			PruneSyncBarrier();
		}

		private static bool IsPendingClientStillExpected(ulong clientId)
		{
			if (_syncBarrier.IsLoading(clientId, TimeSpan.FromSeconds(LoadingLeaseSeconds)))
				return true;

			if (MultiplayerSession.ConnectedPlayers.TryGetValue(clientId, out var player))
				return player.Connection != null;

			return NetworkConfig.TransportServer is ONI_Together.Networking.Transport.Lan.RiptideServer server
				&& server.IsClientLoading(clientId);
		}

		internal static bool TransferSyncBarrierClient(
			ulong oldClientId,
			ulong newClientId,
			ulong reconnectToken)
		{
			if (!MultiplayerSession.ConnectedPlayers.TryGetValue(newClientId, out var player))
				return false;
			if (!_syncBarrier.IsLoading(
				    oldClientId, TimeSpan.FromSeconds(LoadingLeaseSeconds)))
				return false;
			if (!_syncBarrier.CanReplace(oldClientId, newClientId, reconnectToken)
			    || !ReliableSyncBacklog.CanTransfer(oldClientId, newClientId))
				return false;
			if (!_syncBarrier.Replace(oldClientId, newClientId, reconnectToken))
				return false;
			if (!ReliableSyncBacklog.Transfer(oldClientId, newClientId))
			{
				_syncBarrier.Replace(newClientId, oldClientId, reconnectToken);
				return false;
			}

			player.readyState = ClientReadyState.Loading;
			return true;
		}

		internal static bool HasReconnectProof(ulong clientId, ulong reconnectToken)
			=> _syncBarrier.IsLoading(clientId, TimeSpan.FromSeconds(LoadingLeaseSeconds))
			   && _syncBarrier.HasReconnectProof(clientId, reconnectToken);

		internal static bool IsClientInSyncBarrier(ulong clientId)
			=> _syncBarrier.Contains(clientId);

		internal static bool IsCurrentSnapshot(ulong clientId, long snapshotGeneration)
			=> _syncBarrier.MatchesGeneration(clientId, snapshotGeneration);

		internal static void AbortSyncBarrier(ulong clientId)
		{
			WorldDataRequestPacket.CancelTransfer(clientId);
			RemoveActiveLanLoadingProof(clientId);
			bool completed = _syncBarrier.Complete(clientId);
			ReliableSyncBacklog.Clear(clientId);
			FinishSyncBarrierIfNeeded(completed);
		}

		internal static void ResetSessionState()
		{
			bool restoreAutomaticPause = ShouldRestoreAutomaticPauseOnReset(
				_syncBarrier.IsActive,
					_syncBarrier.WasPausedBeforeStart,
					_ownsAutomaticPause);
			SpeedControlScreen speed = SpeedControlScreen.Instance;
			RestoreAutomaticPauseOnReset(
				restoreAutomaticPause,
				speed != null && speed.IsPaused
					? () => SetPauseWithoutLocalPatch(speed, paused: false)
					: null);
			_syncBarrier.Reset();
			ReliableSyncBacklog.ClearAll();
			Interlocked.Exchange(ref _nextSnapshotGeneration, 0);
			_reconnectToken = 0;
			_clientSnapshotGeneration = 0;
			_pendingLoadingToken = 0;
			_pendingWorldLoad = null;
			_completedReadyProofs.Clear();
			_nextBarrierPruneAt = 0f;
			_ownsAutomaticPause = false;
		}

		internal static bool ShouldRestoreAutomaticPauseOnReset(
			bool barrierActive,
			bool wasPausedBeforeStart,
			bool ownsAutomaticPause)
			=> barrierActive && !wasPausedBeforeStart && ownsAutomaticPause;

		internal static void MarkAutomaticPauseOwnership()
		{
			_ownsAutomaticPause = true;
		}

		internal static void ClearAutomaticPauseOwnership()
		{
			if (ShouldClearAutomaticPauseOwnership(_syncBarrier.IsActive))
				_ownsAutomaticPause = false;
		}

		internal static bool ShouldClearAutomaticPauseOwnership(bool barrierActive)
			=> !barrierActive;

		internal static bool ShouldReleaseAutomaticPause(
			bool barrierShouldUnpause,
			bool ownsAutomaticPause)
			=> barrierShouldUnpause && ownsAutomaticPause;

		private static void FinishSyncBarrierIfNeeded(bool changed)
		{
			if (!changed || _syncBarrier.IsActive)
				return;

			SpeedControlScreen speed = SpeedControlScreen.Instance;
			if (ShouldReleaseAutomaticPause(
				    _syncBarrier.ShouldUnpauseAfterCompletion,
				    _ownsAutomaticPause))
			{
				if (speed != null)
					SetPauseWithoutLocalPatch(speed, paused: false);
			}
			_ownsAutomaticPause = false;
			if (speed != null)
			{
				SpeedChangePacket.SpeedState state = speed.IsPaused
					? SpeedChangePacket.SpeedState.Paused
					: (SpeedChangePacket.SpeedState)speed.GetSpeed();
				SpeedChangePacket.SubmitLocalChange(state);
			}
			GameServerHardSync.OnSyncBarrierCompleted();
		}

		private static void SetPauseWithoutLocalPatch(SpeedControlScreen speed, bool paused)
		{
			ONI_Together.Patches.SpeedControlScreen_SendSpeedPacketPatch.IsSyncing = true;
			try
			{
				if (paused)
					speed.Pause(false);
				else
					speed.Unpause(false);
			}
			finally
			{
				ONI_Together.Patches.SpeedControlScreen_SendSpeedPacketPatch.IsSyncing = false;
			}
		}

	}
}
