using System;
using System.Collections.Generic;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Transport.Lan;

namespace ONI_Together.Networking
{
	internal enum ReconnectProofStatus
	{
		Missing,
		Active,
		Completed
	}

	public partial class ReadyManager
	{
		private static readonly CompletedReadyProofLedger _completedReadyProofs = new();
		private static readonly Dictionary<ulong, PendingReadyCommit> _pendingReadyCommits = new();

		private sealed class PendingReadyCommit
		{
			internal MultiplayerPlayer Player;
			internal object Connection;
			internal ulong ReconnectToken;
			internal long SnapshotGeneration;
		}

		internal static void CancelPendingClientWorldLoad()
		{
			_reconnectToken = 0;
			_pendingLoadingToken = 0;
			_pendingWorldLoad = null;
			GameClient.CancelWorldLoadPhase();
		}

		internal static void PrepareFreshSnapshot(ulong clientId)
		{
			CancelPendingReadyCommit(clientId);
			SaveFileTransferManager.CancelTransfers(clientId);
			if (NetworkConfig.TransportServer is RiptideServer server)
				server.TcpTransfer?.CancelTransfers(clientId);
			if (MultiplayerSession.ConnectedPlayers.TryGetValue(clientId, out MultiplayerPlayer player))
				player.CompleteSaveTransfer();
			AbortSyncBarrier(clientId);
		}

		internal static bool TrySendWorldLoadAbort()
		{
			if (MultiplayerSession.IsHost || _reconnectToken == 0
			    || _clientSnapshotGeneration <= 0)
				return false;
			return SendReadyStatusPacket(States.ClientReadyState.Aborted);
		}

		internal static bool TryAbortClientWorldLoad(
			ulong clientId, ulong reconnectToken, long snapshotGeneration)
		{
			if (!_syncBarrier.CanAbort(clientId, reconnectToken, snapshotGeneration))
				return false;

			SaveFileTransferManager.CancelTransfers(clientId);
			if (NetworkConfig.TransportServer is RiptideServer server)
				server.TcpTransfer?.CancelTransfers(clientId);
			if (MultiplayerSession.ConnectedPlayers.TryGetValue(
				    clientId, out MultiplayerPlayer player))
				player.CompleteSaveTransfer();
			AbortSyncBarrier(clientId);
			return true;
		}

		private static bool CommitReadyOrResend(
			MultiplayerPlayer player,
			ulong reconnectToken,
			long snapshotGeneration)
		{
			if (_pendingReadyCommits.TryGetValue(player.PlayerId, out PendingReadyCommit pending))
				return pending.ReconnectToken == reconnectToken
				       && pending.SnapshotGeneration == snapshotGeneration
				       && ReferenceEquals(pending.Connection, player.Connection);
			if (_completedReadyProofs.IsExact(
				    player.PlayerId, reconnectToken, snapshotGeneration))
				return SendReadyAccepted(player.PlayerId, reconnectToken, snapshotGeneration);
			if (!_syncBarrier.CanComplete(
				    player.PlayerId, reconnectToken, snapshotGeneration,
				    player.ConnectionGeneration))
			{
				DebugConsole.LogWarning(
					$"[ReadyManager] Rejected Ready without exact loading proof for {player.PlayerId}");
				return false;
			}
			pending = new PendingReadyCommit
			{
				Player = player,
				Connection = player.Connection,
				ReconnectToken = reconnectToken,
				SnapshotGeneration = snapshotGeneration
			};
			_pendingReadyCommits.Add(player.PlayerId, pending);
			if (!ReliableSyncBacklog.Replay(
				    player,
				    succeeded => CompleteReadyAfterReplay(pending, succeeded)))
			{
				if (_pendingReadyCommits.Remove(player.PlayerId))
					return AbortReadyCommit(player, "Failed to start reliable delta replay");
				return false;
			}
			return true;
		}

		private static void CompleteReadyAfterReplay(
			PendingReadyCommit pending, bool succeeded)
		{
			MultiplayerPlayer player = pending.Player;
			if (!_pendingReadyCommits.TryGetValue(player.PlayerId, out PendingReadyCommit current)
			    || !ReferenceEquals(current, pending))
				return;
			_pendingReadyCommits.Remove(player.PlayerId);
			if (!succeeded || !ReferenceEquals(player.Connection, pending.Connection)
			    || !_syncBarrier.CanComplete(
				    player.PlayerId, pending.ReconnectToken, pending.SnapshotGeneration,
				    player.ConnectionGeneration))
			{
				AbortReadyCommit(player, "Reliable delta replay was not applied");
				return;
			}

			if (!SendReadyAccepted(
				    player.PlayerId, pending.ReconnectToken, pending.SnapshotGeneration))
			{
				AbortReadyCommit(player, "Failed to acknowledge Ready");
				return;
			}
			if (!_completedReadyProofs.Record(
				    player.PlayerId,
				    pending.ReconnectToken,
				    pending.SnapshotGeneration,
				    System.DateTime.UtcNow))
			{
				AbortReadyCommit(player, "Failed to record completed Ready proof");
				return;
			}

			if (NetworkConfig.TransportServer is RiptideServer server)
				server.CompleteLoadingClient(pending.ReconnectToken, player.PlayerId);
			player.CompleteSaveTransfer();
			player.readyState = States.ClientReadyState.Ready;
			CompleteSyncBarrier(player.PlayerId);
			RefreshScreen();
			RefreshReadyState();
		}

		internal static void CancelPendingReadyCommit(ulong clientId)
			=> _pendingReadyCommits.Remove(clientId);

		internal static void CancelAllPendingReadyCommits()
			=> _pendingReadyCommits.Clear();

		internal static bool HasPendingReadyCommitForTests(ulong clientId)
			=> _pendingReadyCommits.ContainsKey(clientId);

		private static bool SendReadyAccepted(
			ulong clientId, ulong reconnectToken, long snapshotGeneration)
			=> PacketSender.SendToPlayer(clientId, new ReadyAcceptedPacket
			{
				ReconnectToken = reconnectToken,
				SnapshotGeneration = snapshotGeneration
			}, PacketSendMode.ReliableImmediate);

		private static bool AbortReadyCommit(MultiplayerPlayer player, string reason)
		{
			DebugConsole.LogError($"[ReadyManager] {reason} for {player.PlayerId}", false);
			AbortSyncBarrier(player.PlayerId);
			player.CompleteSaveTransfer();
			NetworkConfig.TransportServer?.KickClient(player.PlayerId);
			return false;
		}

		internal static bool AcknowledgeReadyAccepted(
			ulong clientId, ulong reconnectToken, long snapshotGeneration)
			=> _completedReadyProofs.Acknowledge(
				clientId, reconnectToken, snapshotGeneration);

		internal static ReconnectProofStatus GetReconnectProofStatus(
			ulong clientId, ulong reconnectToken, bool requireSameCompletedClient)
		{
			if (HasReconnectProof(clientId, reconnectToken))
			{
				if (MultiplayerSession.ConnectedPlayers.TryGetValue(
					    clientId, out MultiplayerPlayer player)
				    && _syncBarrier.RequiresFreshSnapshotAfterConnectionChange(
					    clientId, player.ConnectionGeneration))
				{
					return ReconnectProofStatus.Missing;
				}
				return ReconnectProofStatus.Active;
			}
			return _completedReadyProofs.AuthorizesReconnect(
				clientId, reconnectToken, requireSameCompletedClient)
				? ReconnectProofStatus.Completed
				: ReconnectProofStatus.Missing;
		}

		private static void RemoveActiveLanLoadingProof(ulong clientId)
		{
			if (NetworkConfig.TransportServer is not RiptideServer server
			    || !_syncBarrier.TryGetProof(clientId, out ulong token, out _))
				return;
			server.CompleteLoadingClient(token, clientId);
		}

		private static void PruneCompletedReadyProofs(System.DateTime utcNow)
		{
			int removed = _completedReadyProofs.Prune(
				utcNow, System.TimeSpan.FromSeconds(LoadingLeaseSeconds));
			if (removed > 0)
				DebugConsole.Log($"[ReadyManager] Pruned {removed} completed Ready proof(s)");
		}

		internal static bool RestoreAutomaticPauseOnReset(
			bool shouldRestore,
			System.Action restorePause)
		{
			if (!shouldRestore || restorePause == null)
				return false;
			restorePause();
			return true;
		}
	}
}
