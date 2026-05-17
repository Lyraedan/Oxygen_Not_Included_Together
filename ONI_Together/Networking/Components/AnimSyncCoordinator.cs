using System.Collections.Generic;
using System.Diagnostics;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Animation;
using Shared.Profiling;
using Steamworks;
using UnityEngine;

namespace ONI_Together.Networking.Components
{
	internal class AnimSyncCoordinator : MonoBehaviour
	{
		private class SyncState
		{
			public bool HasObservedState;
			public int LastActivityKey;
			public float LastSentTime;
		}

		private const float TickInterval = 0.2f;
		private const int ShardCount = 5;
		private const float VisibleSyncInterval = 5f;
		// Minimum gap between activity-triggered sends per entity. Bounds bandwidth
		// when activity-key quantization (speed / operational bits) flaps at tick rate.
		private const float ActivityChangeMinInterval = 2f;
		private const int VisibilityMargin = 2;
		private const int PendingUnreliableBackoffBytes = 32768;
		private const long QueueTimeBackoffUsec = 100000;

		private static readonly HashSet<AnimStateSyncer> TrackedSyncers = [];

		public static AnimSyncCoordinator Instance { get; private set; }

		private readonly Dictionary<AnimStateSyncer, SyncState> _syncStates = [];
		private readonly Dictionary<ulong, HashSet<int>> _pendingResyncRequests = [];
		private readonly HashSet<ulong> _visibleRecipients = [];
		private float _tickTimer;
		private int _currentShard;

		// Observability counters (reset on log flush every ~30s).
		private int _sendsActivity;
		private int _sendsInterval;
		private int _skipsOffscreen;
		private float _lastStatsLogTime;
		private const float StatsLogInterval = 30f;

		private void Awake()
		{
			using var _ = Profiler.Scope();

			Instance = this;
		}

		private void OnDestroy()
		{
			using var _ = Profiler.Scope();

			if (Instance == this)
				Instance = null;
		}

		public static void Register(AnimStateSyncer syncer)
		{
			using var _ = Profiler.Scope();

			if (syncer != null)
				TrackedSyncers.Add(syncer);
		}

		public static void Unregister(AnimStateSyncer syncer)
		{
			using var _ = Profiler.Scope();

			if (syncer != null)
			{
				TrackedSyncers.Remove(syncer);
				Instance?._syncStates.Remove(syncer);
			}
		}

		public static List<AnimStateSyncer> GetTrackedSyncers()
		{
			using var _ = Profiler.Scope();

			var syncers = new List<AnimStateSyncer>(TrackedSyncers.Count);
			foreach (var syncer in TrackedSyncers)
			{
				if (syncer != null)
					syncers.Add(syncer);
			}
			return syncers;
		}

		public void QueueResyncRequest(ulong requesterId, IEnumerable<int> netIds)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost || !MultiplayerSession.InSession)
				return;

			if (!MultiplayerSession.ConnectedPlayers.TryGetValue(requesterId, out var player) || player.Connection == null)
				return;

			if (!_pendingResyncRequests.TryGetValue(requesterId, out var requestSet))
			{
				requestSet = [];
				_pendingResyncRequests[requesterId] = requestSet;
			}

			foreach (var netId in netIds)
			{
				if (netId != 0)
					requestSet.Add(netId);
			}
		}

		private void Update()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost)
				return;

			if (MultiplayerSession.ConnectedPlayers.Count == 0)
				return;

			_tickTimer += Time.unscaledDeltaTime;
			while (_tickTimer >= TickInterval)
			{
				_tickTimer -= TickInterval;
				RunTick();
			}
		}

		private void RunTick()
		{
			using var _ = Profiler.Scope();

			ProcessPendingRequests();

			var trackedSyncers = GetTrackedSyncers();
			if (trackedSyncers.Count == 0)
				return;

			bool applyBackoff = ShouldBackOffForSteamQueue();

			// Spread the full scan across five 200ms ticks to avoid bursty correction traffic.
			for (int i = 0; i < trackedSyncers.Count; i++)
			{
				if (i % ShardCount != _currentShard)
					continue;

				ProcessSyncer(trackedSyncers[i], applyBackoff);
			}

			_currentShard = (_currentShard + 1) % ShardCount;

			MaybeLogStats(Time.unscaledTime);
		}

		private void ProcessPendingRequests()
		{
			using var _ = Profiler.Scope();

			if (_pendingResyncRequests.Count == 0)
				return;

			float now = Time.unscaledTime;
			var completed = new List<ulong>();

			foreach (var kvp in _pendingResyncRequests)
			{
				if (!MultiplayerSession.ConnectedPlayers.TryGetValue(kvp.Key, out var player) || player.Connection == null)
				{
					completed.Add(kvp.Key);
					continue;
				}

				foreach (var netId in kvp.Value)
				{
					if (!NetworkIdentityRegistry.TryGet(netId, out var identity))
						continue;
					if (!identity.TryGetComponent<AnimStateSyncer>(out var syncer))
						continue;
					if (!syncer.TryBuildSnapshot(out var packet, out var activityKey))
						continue;

                    if (!_syncStates.ContainsKey(syncer))
                    {
                        DebugConsole.LogError($"Syncer not registered: {syncer}");
                        continue;
                    }

                    // Reliable: resync responses are small and low-frequency; a dropped
                    // response otherwise cascades into another client retry loop.
                    PacketSender.SendToPlayer(kvp.Key, packet, PacketSendMode.Reliable);
					UpdateObservedState(syncer, activityKey, now);
					_syncStates[syncer].LastSentTime = now;
				}

				completed.Add(kvp.Key);
			}

			foreach (var requesterId in completed)
				_pendingResyncRequests.Remove(requesterId);
		}

		private void ProcessSyncer(AnimStateSyncer syncer, bool applyBackoff)
		{
			using var _ = Profiler.Scope();

			if (syncer == null)
				return;

			if (!syncer.TryBuildSnapshot(out var packet, out var activityKey))
				return;

			float now = Time.unscaledTime;
			bool activityChanged = UpdateObservedState(syncer, activityKey, now);
			bool visible = TryCollectVisibleRecipients(syncer);

			// No viewer, no send. Off-screen fan-out to all clients wasted bandwidth
			// across distant viewports; request-based resync covers join-in-progress,
			// and the next shard visit resyncs when a client re-enters the cell.
			if (!visible)
			{
				_skipsOffscreen++;
				return;
			}

			var syncState = _syncStates[syncer];

			// Activity-triggered fast path, gated by a minimum interval so that
			// quantization noise in activityKey cannot drive 1/shard-tick sends.
			if (activityChanged && now - syncState.LastSentTime >= ActivityChangeMinInterval)
			{
				SendSnapshotToVisibleClients(packet, syncer, now);
				_sendsActivity++;
				return;
			}

			float interval = VisibleSyncInterval;
			if (applyBackoff)
				interval *= 2f;

			if (now - syncState.LastSentTime < interval)
				return;

			SendSnapshotToVisibleClients(packet, syncer, now);
			_sendsInterval++;
		}

		private void MaybeLogStats(float now)
		{
			using var _ = Profiler.Scope();

			if (_lastStatsLogTime == 0f)
			{
				_lastStatsLogTime = now;
				return;
			}
			if (now - _lastStatsLogTime < StatsLogInterval)
				return;

			DebugConsole.Log($"[AnimSync] window={StatsLogInterval:F0}s sends(activity)={_sendsActivity} sends(interval)={_sendsInterval} offscreen-skipped={_skipsOffscreen} tracked={TrackedSyncers.Count}");
			_sendsActivity = 0;
			_sendsInterval = 0;
			_skipsOffscreen = 0;
			_lastStatsLogTime = now;
		}

		private bool UpdateObservedState(AnimStateSyncer syncer, int activityKey, float now)
		{
			using var _ = Profiler.Scope();

			if (!_syncStates.TryGetValue(syncer, out var syncState))
			{
				syncState = new SyncState();
				_syncStates[syncer] = syncState;
			}

			bool activityChanged = !syncState.HasObservedState || syncState.LastActivityKey != activityKey;
			if (activityChanged)
			{
				syncState.HasObservedState = true;
				syncState.LastActivityKey = activityKey;
			}

			return activityChanged;
		}

		private bool TryCollectVisibleRecipients(AnimStateSyncer syncer)
		{
			using var _ = Profiler.Scope();

			_visibleRecipients.Clear();
			if (WorldStateSyncer.Instance == null)
				return false;

			WorldStateSyncer.Instance.GetClientsViewingCell(syncer.GetGridCell(), _visibleRecipients, VisibilityMargin);
			return _visibleRecipients.Count > 0;
		}

		private void SendSnapshotToVisibleClients(AnimSyncPacket packet, AnimStateSyncer syncer, float now)
		{
			using var _ = Profiler.Scope();

			var sw = Stopwatch.StartNew();
			int bytes = 0;
			try { bytes = packet.SerializeToByteArray().Length; } catch { }
			foreach (var recipient in _visibleRecipients)
				PacketSender.SendToPlayer(recipient, packet, PacketSendMode.Unreliable);

			_syncStates[syncer].LastSentTime = now;
			sw.Stop();
			SyncStats.RecordSync(SyncStats.AnimSync, _visibleRecipients.Count, bytes, (float)sw.Elapsed.TotalMilliseconds);
		}

		private bool ShouldBackOffForSteamQueue()
		{
			using var _ = Profiler.Scope();

			if (!NetworkConfig.IsSteamConfig())
				return false;

			foreach (var player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (player.PlayerId == MultiplayerSession.HostUserID)
					continue;
				if (player.Connection is not HSteamNetConnection connection)
					continue;

				SteamNetConnectionRealTimeStatus_t status = default;
				SteamNetConnectionRealTimeLaneStatus_t laneStatus = default;
				var result = SteamNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref laneStatus);
				if (result != EResult.k_EResultOK)
					continue;

				// Queue pressure doubles correction intervals before unreliable traffic starts piling up.
				if (status.m_cbPendingUnreliable > PendingUnreliableBackoffBytes || (long)status.m_usecQueueTime > QueueTimeBackoffUsec)
					return true;
			}

			return false;
		}

	}
}
