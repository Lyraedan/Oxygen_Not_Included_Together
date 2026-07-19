using ONI_Together.Networking.States;
using System;
using System.Collections.Generic;

namespace ONI_Together.Networking
{
	internal sealed class SyncBarrier
	{
		private sealed class ClientState
		{
			public System.DateTime BarrierStartedAtUtc;
			public long SnapshotGeneration;
			public ulong ReconnectToken;
			public bool LoadingAccepted;
			public System.DateTime LoadingAcceptedAtUtc;
			public bool WorldBaselineStarted;
			public long WorldBaselineConnectionGeneration;
		}

		private readonly Dictionary<ulong, ClientState> _pendingClients = new();
		private bool _started;

		public int PendingCount => _pendingClients.Count;
		public bool IsActive => PendingCount > 0;
		public bool WasPausedBeforeStart { get; private set; }
		public bool ShouldUnpauseAfterCompletion => _started && !IsActive && !WasPausedBeforeStart;

		public bool Add(
			ulong clientId,
			bool isPaused,
			System.DateTime? startedAtUtc = null,
			bool pauseOwnedBySession = false)
		{
			if (!IsActive)
			{
				WasPausedBeforeStart = isPaused && !pauseOwnedBySession;
				_started = true;
			}

			if (_pendingClients.ContainsKey(clientId))
				return false;
			_pendingClients.Add(clientId, new ClientState
			{
				BarrierStartedAtUtc = startedAtUtc ?? System.DateTime.UtcNow
			});
			return true;
		}

		public bool Complete(ulong clientId)
		{
			return _pendingClients.Remove(clientId);
		}

		public bool Replace(ulong oldClientId, ulong newClientId, ulong reconnectToken)
		{
			if (!CanReplace(oldClientId, newClientId, reconnectToken))
				return false;
			if (oldClientId == newClientId)
				return true;

			ClientState state = _pendingClients[oldClientId];
			_pendingClients.Remove(oldClientId);
			state.LoadingAcceptedAtUtc = System.DateTime.UtcNow;
			_pendingClients.Add(newClientId, state);
			return true;
		}

		public bool CanReplace(ulong oldClientId, ulong newClientId, ulong reconnectToken)
		{
			return _pendingClients.TryGetValue(oldClientId, out ClientState state)
			       && state.LoadingAccepted && reconnectToken != 0
			       && state.ReconnectToken == reconnectToken
			       && (oldClientId == newClientId || !_pendingClients.ContainsKey(newClientId));
		}

		public bool Contains(ulong clientId)
		{
			return _pendingClients.ContainsKey(clientId);
		}

		public bool MatchesGeneration(ulong clientId, long snapshotGeneration)
			=> snapshotGeneration > 0
			   && _pendingClients.TryGetValue(clientId, out ClientState state)
			   && state.SnapshotGeneration == snapshotGeneration;

		public bool Prune(Func<ulong, bool> isConnected)
		{
			return Prune(isConnected, TimeSpan.MaxValue, System.DateTime.UtcNow, null);
		}

		public bool Prune(
			Func<ulong, bool> isConnected,
			TimeSpan maximumLifetime,
			System.DateTime utcNow,
			ICollection<ulong> expiredClients)
		{
			bool changed = false;
			foreach (ulong id in new List<ulong>(_pendingClients.Keys))
			{
				ClientState state = _pendingClients[id];
				bool expired = state.BarrierStartedAtUtc != default
				               && utcNow - state.BarrierStartedAtUtc > maximumLifetime;
				if (isConnected(id) && !expired)
					continue;
				_pendingClients.Remove(id);
				if (expired)
					expiredClients?.Add(id);
				changed = true;
			}
			return changed;
		}

		public bool MarkTransferStarted(ulong clientId, long snapshotGeneration)
		{
			if (snapshotGeneration <= 0 || !_pendingClients.TryGetValue(clientId, out ClientState state))
				return false;
			state.SnapshotGeneration = snapshotGeneration;
			state.ReconnectToken = 0;
			state.LoadingAccepted = false;
			state.LoadingAcceptedAtUtc = default;
			state.WorldBaselineStarted = false;
			state.WorldBaselineConnectionGeneration = 0;
			return true;
		}

		public bool TryBeginWorldBaseline(
			ulong clientId,
			long snapshotGeneration,
			long connectionGeneration = 0)
		{
			if (!MatchesGeneration(clientId, snapshotGeneration)
			    || !_pendingClients.TryGetValue(clientId, out ClientState state)
			    || !state.LoadingAccepted || state.WorldBaselineStarted)
			{
				return false;
			}
			state.WorldBaselineStarted = true;
			if (connectionGeneration <= 0
			    && MultiplayerSession.ConnectedPlayers.TryGetValue(
				    clientId, out MultiplayerPlayer player))
				connectionGeneration = player.ConnectionGeneration;
			state.WorldBaselineConnectionGeneration = connectionGeneration;
			return true;
		}

		public bool RequiresFreshSnapshotAfterConnectionChange(
			ulong clientId, long connectionGeneration)
		{
			return _pendingClients.TryGetValue(clientId, out ClientState state)
			       && state.WorldBaselineStarted
			       && state.WorldBaselineConnectionGeneration > 0
			       && state.WorldBaselineConnectionGeneration != connectionGeneration;
		}

		public bool TryAcceptLoading(ulong clientId, ulong reconnectToken, long snapshotGeneration)
		{
			if (reconnectToken == 0 || !_pendingClients.TryGetValue(clientId, out ClientState state)
			    || snapshotGeneration <= 0 || state.SnapshotGeneration != snapshotGeneration
			    || state.ReconnectToken != 0 && state.ReconnectToken != reconnectToken)
				return false;
			state.ReconnectToken = reconnectToken;
			state.LoadingAccepted = true;
			state.LoadingAcceptedAtUtc = System.DateTime.UtcNow;
			return true;
		}

		public bool CanComplete(
			ulong clientId,
			ulong reconnectToken,
			long snapshotGeneration,
			long connectionGeneration = 0)
			=> reconnectToken != 0 && _pendingClients.TryGetValue(clientId, out ClientState state)
			   && snapshotGeneration > 0 && state.SnapshotGeneration == snapshotGeneration
			   && state.LoadingAccepted && state.WorldBaselineStarted
			   && state.ReconnectToken == reconnectToken
			   && (connectionGeneration <= 0
			       || state.WorldBaselineConnectionGeneration == connectionGeneration);

		public bool CanAbort(ulong clientId, ulong reconnectToken, long snapshotGeneration)
			=> reconnectToken != 0 && _pendingClients.TryGetValue(clientId, out ClientState state)
			   && snapshotGeneration > 0 && state.SnapshotGeneration == snapshotGeneration
			   && state.LoadingAccepted && state.ReconnectToken == reconnectToken;

		public bool IsLoading(ulong clientId, TimeSpan maximumAge)
			=> _pendingClients.TryGetValue(clientId, out ClientState state)
			   && state.LoadingAccepted && state.LoadingAcceptedAtUtc != default
			   && System.DateTime.UtcNow - state.LoadingAcceptedAtUtc <= maximumAge;

		public bool HasReconnectProof(ulong clientId, ulong reconnectToken)
			=> reconnectToken != 0 && _pendingClients.TryGetValue(clientId, out ClientState state)
			   && state.LoadingAccepted && state.ReconnectToken == reconnectToken;

		public bool TryGetProof(ulong clientId, out ulong reconnectToken, out long snapshotGeneration)
		{
			reconnectToken = 0;
			snapshotGeneration = 0;
			if (!_pendingClients.TryGetValue(clientId, out ClientState state)
			    || !state.LoadingAccepted || state.ReconnectToken == 0)
				return false;
			reconnectToken = state.ReconnectToken;
			snapshotGeneration = state.SnapshotGeneration;
			return snapshotGeneration > 0;
		}

		public void Reset()
		{
			_pendingClients.Clear();
			_started = false;
			WasPausedBeforeStart = false;
		}

		public static bool IsExactReady(ClientReadyState state)
		{
			return state == ClientReadyState.Ready;
		}

		public static bool IsValidReadyState(ClientReadyState state)
		{
			return state == ClientReadyState.Ready
				|| state == ClientReadyState.Unready
				|| state == ClientReadyState.Loading
				|| state == ClientReadyState.Aborted;
		}

		public static bool SenderMatches(ulong payloadSenderId, ulong transportSenderId)
		{
			return payloadSenderId == transportSenderId;
		}
	}
}
