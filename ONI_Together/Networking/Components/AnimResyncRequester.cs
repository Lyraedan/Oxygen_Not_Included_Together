using System.Collections.Generic;
using System.Diagnostics;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Animation;
using Shared;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Components
{
	internal class AnimResyncRequester : MonoBehaviour
	{
		private const float InitialRequestDelay = 1f;
		private const float RetryIntervalBase = 5f;
		private const float RetryIntervalMax = 30f;
		// Per-NetId cooldown prevents the same entity from being requested
		// every retry tick when the host silently drops responses.
		private const float PerNetIdCooldown = 15f;
		// Hard cap on NetIds per AnimResyncRequestPacket. Unreliable UDP
		// fragments silently when the payload exceeds MTU.
		private const int MaxNetIdsPerPacket = 64;

		private bool _subscribed;
		private bool _initialRequestSent;
		private float _nextInitialRequestTime = float.MaxValue;
		private float _lastRetryTime;
		private float _retryInterval = RetryIntervalBase;
		private readonly Dictionary<int, float> _lastRequestTime = [];

		private void Update()
		{
			using var _ = Profiler.Scope();

			if (Game.Instance != null && !_subscribed)
				SubscribeToGameHashes();

			if (!MultiplayerSession.IsClient || !MultiplayerSession.InActiveSession || !Utils.IsInGame())
			{
				_initialRequestSent = false;
				_nextInitialRequestTime = float.MaxValue;
				_retryInterval = RetryIntervalBase;
				_lastRequestTime.Clear();
				return;
			}

			if (!_initialRequestSent && _nextInitialRequestTime == float.MaxValue)
				_nextInitialRequestTime = Time.unscaledTime + InitialRequestDelay;

			float now = Time.unscaledTime;
			if (!_initialRequestSent && now >= _nextInitialRequestTime && RequestVisibleAnimations(true))
			{
				_initialRequestSent = true;
				_nextInitialRequestTime = float.MaxValue;
				_lastRetryTime = now;
			}

			if (_initialRequestSent && now - _lastRetryTime >= _retryInterval)
			{
				bool sentAny = RequestVisibleAnimations(false);
				_lastRetryTime = now;
				// Exponential backoff while the host keeps dropping or ignoring
				// our requests; reset on the next fresh session or scheduling.
				if (sentAny)
					_retryInterval = Mathf.Min(_retryInterval * 1.5f, RetryIntervalMax);
			}
		}

		private void SubscribeToGameHashes()
		{
			using var _ = Profiler.Scope();

			Game.Instance.Subscribe(MP_HASHES.GameClient_OnConnectedInGame, ScheduleInitialRequest);
			Game.Instance.Subscribe(MP_HASHES.OnMultiplayerGameSessionInitialized, ScheduleInitialRequest);
			_subscribed = true;
		}

		private void ScheduleInitialRequest(object _ = null)
		{
			using var scope = Profiler.Scope();

			_initialRequestSent = false;
			_nextInitialRequestTime = Time.unscaledTime + InitialRequestDelay;
			_lastRetryTime = 0f;
			_retryInterval = RetryIntervalBase;
			_lastRequestTime.Clear();
		}

		private bool RequestVisibleAnimations(bool includeAllVisible)
		{
			using var _ = Profiler.Scope();

			if (!WorldStateSyncer.TryGetLocalViewport(out var viewport, 2))
				return false;

			float now = Time.unscaledTime;
			var requestedNetIds = new List<int>();
			foreach (var syncer in AnimSyncCoordinator.GetTrackedSyncers())
			{
				if (syncer == null || !syncer.IsVisibleIn(viewport))
					continue;
				// Only retry visible entities that never received an authoritative snapshot.
				if (!includeAllVisible && !syncer.NeedsInitialSnapshot())
					continue;
				if (syncer.NetId == 0)
					continue;

				// Per-NetId cooldown: do not re-request the same entity faster than
				// the host can reasonably respond; avoids flood when responses drop.
				if (_lastRequestTime.TryGetValue(syncer.NetId, out var last) && now - last < PerNetIdCooldown)
					continue;

				requestedNetIds.Add(syncer.NetId);
				_lastRequestTime[syncer.NetId] = now;

				if (requestedNetIds.Count >= MaxNetIdsPerPacket)
					break;
			}

			if (requestedNetIds.Count > 0)
			{
				var sw = Stopwatch.StartNew();
				var packet = new AnimResyncRequestPacket
				{
					RequesterId = MultiplayerSession.LocalUserID,
					NetIds = [.. requestedNetIds]
				};
				int bytes = 0;
				try { bytes = packet.SerializeToByteArray().Length; } catch { }
				PacketSender.SendToHost(packet, PacketSendMode.Unreliable);
				sw.Stop();

				// Record as item=NetIds, bytes=packet size, duration=current retry interval (ms).
				// Interval-in-ms lives in LastDurationMs so log-grep sees backoff value without new fields.
				SyncStats.RecordSync(SyncStats.AnimResyncRequest, requestedNetIds.Count, bytes, _retryInterval * 1000f);
				DebugConsole.Log($"[AnimResyncRequest] netIds={requestedNetIds.Count} bytes={bytes} cap={MaxNetIdsPerPacket} retryInterval={_retryInterval:F1}s initial={includeAllVisible}");
				return true;
			}

			return false;
		}
	}
}
