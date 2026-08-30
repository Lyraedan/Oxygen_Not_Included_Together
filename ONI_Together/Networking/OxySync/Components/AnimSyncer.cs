using System.Linq;
using System.Collections.Generic;
using ONI_Together.DebugTools;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
	public class AnimSyncer : NetworkBehaviour
	{
		[MyCmpGet]
		private KBatchedAnimController animController;

		private ulong _sequenceNumber;
		private float _lastTimestamp;

		private const float ALLOWED_TIMESTAMP_DRIFT = 0.5f; // seconds

		// Use a heartbeat interval to periodically sync animation state to clients.
		private const float HEARTBEAT_INTERVAL = 0.2f;
		private float _timer = 0f;

		// The client can pause the heartbeat for a short duration to avoid unnecessary syncs package
		private const float MAX_PAUSE_DURATION = 1f;
		private float _lastProcessTimestamp = 0f;
		private readonly Dictionary<ulong, float> _pauseRequestsByUser = new();
		private readonly Dictionary<ulong, float> _lastPauseTimestampByUser = new();

		private int localPlayCount = 0;
		private int localSyncCount = 0;
		private static int playCount = 0;
		private static int syncCount = 0;

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();

			base.OnSpawn();
			_sequenceNumber = 0;
			_lastTimestamp = 0f;
			_lastProcessTimestamp = Time.unscaledTime;
			_pauseRequestsByUser.Clear();
			_lastPauseTimestampByUser.Clear();
		}


		public override void OnCleanUp()
		{
			using var _ = Profiler.Scope();

			_pauseRequestsByUser.Clear();
			_lastPauseTimestampByUser.Clear();
			base.OnCleanUp();
		}

		public void RequestToPlayAnim(bool queueing, HashedString[] animNames, KAnim.PlayMode mode, float speed = 1f, float timeOffset = 0f)
		{
			using var _ = Profiler.Scope();

			if (!isServer || !MultiplayerSession.SessionHasPlayers)
			{
				return;
			}

			try
			{
				_sequenceNumber++;
				CallClientRpc(nameof(RpcPlayAnim), _sequenceNumber, Time.unscaledTime, queueing, animNames, (byte)mode, speed, timeOffset);

				// Reset the heartbeat timer to avoid sending an immediate sync packet after the animation play request.
				_timer = 0f;
				_pauseRequestsByUser.Clear();
				_lastProcessTimestamp = Time.unscaledTime;

				DebugConsole.Log($"[AnimSyncer] Sent animation packet: {animNames.FirstOrDefault()} (mode: {mode}, speed: {speed}, timeOffset: {timeOffset})");
			}
			catch (System.Exception e)
			{
				DebugConsole.LogError($"[OxySync] Failed to send animation packet: {e}");
			}
		}

		[Command(SendMode = (int)PacketSendMode.UnreliableImmediate)]
		private void CmdUpdateSyncStatus(float timestamp, float pauseDuration, ulong senderId)
		{
			using var _ = Profiler.Scope();

			if (_lastPauseTimestampByUser.TryGetValue(senderId, out var lastTimestamp) && timestamp <= lastTimestamp)
				return;
			_lastPauseTimestampByUser[senderId] = timestamp;

			// adjust the pause duration to time drift between the server and client
			float timeDrift = Time.unscaledTime - timestamp;
			pauseDuration = Mathf.Min(pauseDuration - timeDrift, MAX_PAUSE_DURATION);
			if (pauseDuration > 0)
			{
				_pauseRequestsByUser[senderId] = pauseDuration;
			}
			else
			{
				_pauseRequestsByUser.Remove(senderId);
			}

			DebugConsole.Log($"[AnimSyncer] Received animation sync: sender={senderId}, pause={pauseDuration}, timestamp={timestamp}, timeDrift={timeDrift}");
		}

		[ClientRpc(SendMode = (int)PacketSendMode.UnreliableImmediate, InterestGroup = -1)]
		private void RpcPlayAnim(ulong sequenceNumber, float timestamp, bool queueing, HashedString[] animNames, byte mode, float speed, float timeOffset)
		{
			using var _ = Profiler.Scope();

			if (sequenceNumber <= _sequenceNumber || (sequenceNumber > 0 && _sequenceNumber < 0))
				return;

			bool isNewerTimestamp = timestamp > _lastTimestamp;
			if (isNewerTimestamp)
				_lastTimestamp = timestamp;
			
			_sequenceNumber = sequenceNumber;

			if (!CanSyncAnim(animNames, out var kbac))
				return;

			PlayAnim(queueing, animNames, (KAnim.PlayMode)mode, speed, timeOffset, false, isNewerTimestamp);
			DebugConsole.Log($"[AnimSyncer] Received animation packet: {animNames.FirstOrDefault()} (mode: {(KAnim.PlayMode)mode}, speed: {speed}, timeOffset: {timeOffset}), localPlayCount: {++localPlayCount}, PlayCount: {++playCount}");
		}

		// Syncs the current animation state to clients. This is called by the server on a heartbeat interval.
		[TargetRpc(SendMode = (int)PacketSendMode.UnreliableImmediate)]
		private void TargetSyncAnim(float timestamp, int animName, byte mode, float speed, float timeOffset)
		{
			using var _ = Profiler.Scope();

			if (timestamp <= _lastTimestamp)
				return;
			_lastTimestamp = timestamp;

			if (!CanSyncAnim([new HashedString(animName)], out var kbac))
				return;

			PlayAnim(false, [new HashedString(animName)], (KAnim.PlayMode)mode, speed, timeOffset, true);

			// If time drift is low, report local animation time-left so host can suppress
			// heartbeat packets to requesters while they keep replaying this animation state.
			if (Mathf.Abs(Time.unscaledTime - timestamp) < ALLOWED_TIMESTAMP_DRIFT)
			{
				CmdUpdateSyncStatus(Time.unscaledTime, GetAnimationTimeLeftSeconds(kbac), MultiplayerSession.LocalUserID);
			}

			DebugConsole.Log($"[AnimSyncer] Synced animation: {animName} (mode: {(KAnim.PlayMode)mode}, speed: {speed}, timeOffset: {timeOffset}), localSyncCount: {++localSyncCount}, syncCount: {++syncCount}");
		}
	
		[Client]
		public void PlayAnim(bool queueing, HashedString[] animNames, KAnim.PlayMode mode, float speed = 1f, float timeOffset = 0f, bool isSync = false, bool forceUpdate = true)
		{
			using var _ = Profiler.Scope();

			if (!CanSyncAnim(animNames, out var kbac))
				return;

			try
			{
				if (animNames.Length > 1)
					kbac.Play(animNames, mode);
				else if (queueing)
					kbac.Queue(animNames.FirstOrDefault(), mode, speed, timeOffset);
				else if (!isSync)
					kbac.Play(animNames.FirstOrDefault(), mode, speed, timeOffset);
				else if (kbac.currentAnim != animNames.FirstOrDefault())
					kbac.Play(animNames.FirstOrDefault(), mode, speed, 0f);

				if (forceUpdate)
					ForceAnimUpdate(kbac);

				if (isSync)
				{
					kbac.SetElapsedTime(timeOffset);
				}
			}
			catch (System.Exception e)
			{
				DebugConsole.LogError($"[AnimSyncer] Failed to play animation on {kbac.gameObject?.GetProperName()}: {e}");
			}
		}

		[Client]
		private void ForceAnimUpdate(KBatchedAnimController kbac)
		{
			using var _ = Profiler.Scope();

			try
			{
				kbac.SetVisiblity(true);
				kbac.forceRebuild = true;
				kbac.SuspendUpdates(false);
				kbac.ConfigureUpdateListener();
			}
			catch (System.Exception e)
			{
				DebugConsole.LogError($"[AnimSyncer] Failed to force animation update on {kbac.gameObject?.GetProperName()}: {e}");
			}

		}

		// Check if the animation can be synced and get the KBatchedAnimController
		// We should only allow the clients to manually change the animation state.
		private bool CanSyncAnim(HashedString[] animNames, out KBatchedAnimController kbc)
		{
			using var _ = Profiler.Scope();

			kbc = null;
			if (!isClient)
				return false;

			if (animController == null)
				animController = GetComponent<KBatchedAnimController>();
			if (animController == null || animNames == null || animNames.Length == 0 || animNames[0] == default)
				return false;

			kbc = animController;
			return true;
		}

		private float GetAnimationTimeLeftSeconds(KBatchedAnimController kbac)
		{
			using var _ = Profiler.Scope();

			if (kbac == null || kbac.currentAnim == default)
				return 0f;

			var anim = kbac.GetAnim(kbac.currentAnim);
			if (anim == null)
				return 0f;

			float total = anim.totalTime;
			if (total <= 0f)
				return 0f;

			float elapsed = kbac.GetElapsedTime();
			float speed = Mathf.Max(Mathf.Abs(kbac.playSpeed), 0.0001f);

			float remainingAnimTime;
			if (kbac.mode == KAnim.PlayMode.Loop)
			{
				// For looping animations
				// we can just return the max pause duration since it will loop indefinitely.
				return MAX_PAUSE_DURATION;
			}
			else
			{
				remainingAnimTime = Mathf.Max(0f, total - elapsed);
			}

			return remainingAnimTime / speed;
		}


		// The server animation heartbeat tick, which sends the current animation state to clients.
		private void TickAnimSync()
		{
			using var _ = Profiler.Scope();

			if (animController.currentAnim == null || animController.currentAnim == default)
				return;

			HashSet<ulong> usersToSync = new HashSet<ulong>(InterestGroupManager.GetGroupMemberIds(InterestGroup));


			foreach (var userId in usersToSync.ToList())
			{
				// Skip syncing to this user if they have requested a pause and the pause duration has not expired.
				if (_pauseRequestsByUser.TryGetValue(userId, out float remainingPause) && remainingPause > 0f)
					continue;

				try
				{
					CallTargetRpc(
						userId,
						nameof(TargetSyncAnim),
						Time.unscaledTime,
						animController.currentAnim.hash,
						(byte)animController.mode,
						animController.playSpeed,
						animController.GetElapsedTime());
				}
				catch (System.Exception e)
				{
					DebugConsole.LogError($"[AnimSyncer] Failed to send animation sync to user {userId}: {e}");
				}
			}
		}

		private void TickProcessPauseDuration()
		{
			using var _ = Profiler.Scope();

			float dt = Time.unscaledTime - _lastProcessTimestamp;
			_lastProcessTimestamp += dt;

			if (_pauseRequestsByUser.Count == 0)
				return;
			
			// Check all cached requester timers and remove any that expired or disconnected.
			// Keep out-of-group requesters cached so they can apply later if they rejoin this group.
			var keys = new List<ulong>(_pauseRequestsByUser.Keys);
			for (int i = 0; i < keys.Count; i++)
			{
				ulong userId = keys[i];

				if (!_pauseRequestsByUser.TryGetValue(userId, out float remaining))
					continue;

				remaining = Mathf.Max(0f, remaining - dt);
				if (remaining <= 0f)
				{
					_pauseRequestsByUser.Remove(userId);
					_lastPauseTimestampByUser.Remove(userId);
				}
				else
				{
					_pauseRequestsByUser[userId] = remaining;
				}
			}
		}

		private void Update()
		{
			using var _ = Profiler.Scope();

			// HeardBeat for syncing animation state to clients.
			if (isServer && MultiplayerSession.SessionHasPlayers && animController != null)
			{
				TickProcessPauseDuration();
				_timer += Time.unscaledDeltaTime;
				if (_timer >= HEARTBEAT_INTERVAL)
				{
					_timer = 0f;

					TickAnimSync();
				}
			}
		}
	}
}
