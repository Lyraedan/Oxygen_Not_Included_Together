using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.DuplicantActions;
using ONI_Together.Patches.KleiPatches;
using Shared.OxySync;
using Shared.Profiling;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ONI_Together.Networking.Components
{
	/// <summary>
	/// Sole client-side visual owner for a networked duplicant. Host navigation
	/// transitions, compact position keyframes and animation events are replayed
	/// on one delayed monotonic timeline; client AI and pathfinding stay disabled.
	/// </summary>
	public sealed class DuplicantClientController : KMonoBehaviour
	{
		[MyCmpGet] private Navigator navigator;
		[MyCmpGet] private KBatchedAnimController animController;

		private const int MaxSnapshotBuffer = 32;
		private const int MaxNavigationBuffer = 24;
		private const int MaxAnimationBuffer = 32;
		private const int MaxStateBuffer = 12;
		private const double BasePlaybackBufferMs = 100.0;
		private const double MaxPlaybackBufferMs = 200.0;
		private const double MaxExtrapolationMs = 80.0;
		private const float SoftCorrectionDeadZone = 0.05f;
		private const float SoftCorrectionGain = 18f;
		private const float HardCorrectionDistance = 1.5f;

		private sealed class TimedSnapshot
		{
			internal DuplicantVisualSnapshot Snapshot;
			internal double LocalTimestamp;
		}

		private sealed class TimedNavigationEvent
		{
			internal NavigatorTransitionPacket Packet;
			internal double LocalTimestamp;
		}

		private sealed class TimedAnimationEvent
		{
			internal PlayAnimPacket Packet;
			internal double LocalTimestamp;
		}

		private sealed class TimedStateEvent
		{
			internal DuplicantStatePacket Packet;
			internal double LocalTimestamp;
		}

		private readonly List<TimedSnapshot> _snapshots = new(MaxSnapshotBuffer);
		private readonly List<TimedNavigationEvent> _navigationEvents = new(MaxNavigationBuffer);
		private readonly List<TimedAnimationEvent> _animationEvents = new(MaxAnimationBuffer);
		private readonly List<TimedStateEvent> _stateEvents = new(MaxStateBuffer);
		private readonly SnapshotTimeline _timeline = new();

		private uint _lastAppliedSnapshotSequence;
		private uint _lastAppliedNavigationSequence;
		private uint _lastAppliedAnimationSequence;
		private uint _lastAppliedStateSequence;
		private long _lastAppliedStateTimestamp;
		private bool _isTransitioning;
		private bool _underflowActive;
		private float _animationSpeedAdjustmentUntil;

		public bool IsPlaybackActive { get; private set; }
		public bool IsMoving => _isTransitioning || _navigationEvents.Count > 0;
		public bool OwnsPosition => IsPlaybackActive;

		public int SnapshotsReceived { get; private set; }
		public int NavigationEventsReceived { get; private set; }
		public int AnimationEventsReceived { get; private set; }
		public int DroppedStaleEvents { get; private set; }
		public int BufferUnderflows { get; private set; }
		public int SoftCorrections { get; private set; }
		public int HardCorrections { get; private set; }

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();
			base.OnSpawn();
			if (navigator == null) navigator = GetComponent<Navigator>();
			if (animController == null) animController = GetComponent<KBatchedAnimController>();

			if (!MultiplayerSession.InActiveSession || !MultiplayerSession.IsClient
				|| navigator == null || animController == null)
			{
				enabled = false;
				return;
			}

			// A saved Navigator state must never keep advancing independently on the
			// client. Stop it once, then drive only its visual TransitionDriver here.
			try
			{
				navigator.Stop(false, false);
			}
			catch (Exception ex)
			{
				DebugConsole.LogWarning($"[DuplicantPlayback] Could not reset Navigator on {gameObject.name}: {ex.Message}");
				navigator.transitionDriver?.EndTransition();
			}

			IsPlaybackActive = true;
		}

		public override void OnCleanUp()
		{
			using var _ = Profiler.Scope();
			CancelTransition();
			IsPlaybackActive = false;
			_snapshots.Clear();
			_navigationEvents.Clear();
			_animationEvents.Clear();
			_stateEvents.Clear();
			base.OnCleanUp();
		}

		private void Update()
		{
			using var _ = Profiler.Scope();
			if (!IsPlaybackActive || !MultiplayerSession.InActiveSession || !MultiplayerSession.IsClient)
				return;

			double now = SnapshotTimeline.MonotonicMilliseconds;
			double bufferMs = _timeline.GetAdaptiveBufferMilliseconds(
				BasePlaybackBufferMs, MaxPlaybackBufferMs);
			bool isPaused = Time.timeScale <= 0.0001f;
			// While paused there is no motion to hide, so retaining the normal visual
			// delay only makes host and client appear to disagree. Drain received state
			// immediately and freeze at the newest authoritative point.
			double playbackTime = isPaused ? now : now - bufferMs;

			ProcessNavigationEvents(playbackTime);
			UpdateActiveTransition();
			ProcessNavigationEvents(playbackTime);
			ApplyBufferedPosition(playbackTime, isPaused);
			ProcessAnimationEvents(playbackTime);
			ProcessStateEvents(playbackTime);
			UpdateAnimationSpeedAdjustment();
		}

		public void OnSnapshotReceived(DuplicantVisualSnapshot snapshot, long serverTimestamp)
		{
			using var _ = Profiler.Scope();
			if (!IsPlaybackActive)
				return;

			if (snapshot.Sequence != 0
				&& !IsNewerSequence(snapshot.Sequence, _lastAppliedSnapshotSequence))
			{
				DroppedStaleEvents++;
				return;
			}
			for (int i = 0; i < _snapshots.Count; i++)
			{
				if (snapshot.Sequence != 0 && _snapshots[i].Snapshot.Sequence == snapshot.Sequence)
					return;
			}

			var timed = new TimedSnapshot
			{
				Snapshot = snapshot,
				LocalTimestamp = MapRemoteTimestamp(serverTimestamp),
			};
			InsertSnapshot(timed);
			SnapshotsReceived++;

			if (_snapshots.Count > MaxSnapshotBuffer)
			{
				MarkSnapshotApplied(_snapshots[0].Snapshot.Sequence);
				_snapshots.RemoveAt(0);
				BufferUnderflows++;
			}
		}

		public void OnNavigationEventReceived(NavigatorTransitionPacket packet)
		{
			using var _ = Profiler.Scope();
			if (!IsPlaybackActive || packet == null)
				return;

			if (!IsNewerSequence(packet.Sequence, _lastAppliedNavigationSequence))
			{
				DroppedStaleEvents++;
				return;
			}
			for (int i = 0; i < _navigationEvents.Count; i++)
			{
				if (_navigationEvents[i].Packet.Sequence == packet.Sequence)
					return;
			}

			var timed = new TimedNavigationEvent
			{
				Packet = packet,
				LocalTimestamp = MapRemoteTimestamp(packet.ServerTimestamp),
			};
			InsertNavigationEvent(timed);
			NavigationEventsReceived++;

			if (_navigationEvents.Count > MaxNavigationBuffer)
			{
				_navigationEvents.RemoveAt(0);
				BufferUnderflows++;
			}
		}

		public void OnAnimationEventReceived(PlayAnimPacket packet)
		{
			using var _ = Profiler.Scope();
			if (!IsPlaybackActive || packet == null || packet.AnimHashes == null || packet.AnimHashes.Length == 0)
				return;

			if (!IsNewerSequence(packet.Sequence, _lastAppliedAnimationSequence))
			{
				DroppedStaleEvents++;
				return;
			}
			for (int i = 0; i < _animationEvents.Count; i++)
			{
				if (_animationEvents[i].Packet.Sequence == packet.Sequence)
					return;
			}

			var timed = new TimedAnimationEvent
			{
				Packet = packet,
				LocalTimestamp = MapRemoteTimestamp(packet.TimeStamp),
			};
			InsertAnimationEvent(timed);
			AnimationEventsReceived++;

			if (_animationEvents.Count > MaxAnimationBuffer)
			{
				_animationEvents.RemoveAt(0);
				BufferUnderflows++;
			}
		}

		public void OnStateReceived(DuplicantStatePacket packet)
		{
			using var _ = Profiler.Scope();
			if (!IsPlaybackActive || packet == null)
				return;

			if (!IsNewerSequence(packet.Sequence, _lastAppliedStateSequence))
			{
				DroppedStaleEvents++;
				return;
			}
			for (int i = 0; i < _stateEvents.Count; i++)
			{
				if (_stateEvents[i].Packet.Sequence == packet.Sequence)
					return;
			}

			var timed = new TimedStateEvent
			{
				Packet = packet,
				LocalTimestamp = MapRemoteTimestamp(packet.ServerTimestamp),
			};
			InsertStateEvent(timed);
			if (_stateEvents.Count > MaxStateBuffer)
				_stateEvents.RemoveAt(0);
		}

		private double MapRemoteTimestamp(long remoteTimestamp)
		{
			double arrival = SnapshotTimeline.MonotonicMilliseconds;
			if (remoteTimestamp <= 0)
				return arrival;
			return _timeline.ToLocalTime(remoteTimestamp, arrival);
		}

		private void ProcessNavigationEvents(double playbackTime)
		{
			while (_navigationEvents.Count > 0 && _navigationEvents[0].LocalTimestamp <= playbackTime)
			{
				var timed = _navigationEvents[0];
				_navigationEvents.RemoveAt(0);
				var packet = timed.Packet;
				if (!IsNewerSequence(packet.Sequence, _lastAppliedNavigationSequence))
				{
					DroppedStaleEvents++;
					continue;
				}

				if (packet.IsStop)
					ApplyNavigationStop(packet);
				else
					BeginNavigationTransition(packet, playbackTime - timed.LocalTimestamp);

				_lastAppliedNavigationSequence = packet.Sequence;
			}
		}

		private void BeginNavigationTransition(NavigatorTransitionPacket packet, double lateMilliseconds)
		{
			if (navigator?.NavGrid?.transitions == null
				|| packet.TransitionId >= navigator.NavGrid.transitions.Length)
			{
				DebugConsole.LogWarning($"[DuplicantPlayback] Invalid transition {packet.TransitionId} for {gameObject.name}");
				CancelTransition();
				ApplyBoundaryPosition(packet.SourcePosition);
				return;
			}

			CancelTransition();
			var transition = navigator.NavGrid.transitions[packet.TransitionId];
			ApplyBoundaryPosition(packet.SourcePosition);
			navigator.SetCurrentNavType(transition.start);

			KAnimControllerBase_Patches.AllowAnims();
			try
			{
				float speed = packet.Speed > 0f ? packet.Speed : navigator.defaultSpeed;
				navigator.transitionDriver.BeginTransition(navigator, transition, speed);
			}
			catch (Exception ex)
			{
				DebugConsole.LogWarning($"[DuplicantPlayback] Transition {packet.TransitionId} failed on {gameObject.name}: {ex}");
				navigator.transitionDriver?.EndTransition();
			}
			finally
			{
				KAnimControllerBase_Patches.ForbidAnims();
			}

			_isTransitioning = navigator.transitionDriver?.GetTransition != null;

			// Normally the buffer makes this zero. If a packet arrived just after its
			// playback deadline, catch up only a bounded amount instead of teleporting.
			if (_isTransitioning && lateMilliseconds > 1.0)
			{
				float catchUp = Mathf.Min((float)(lateMilliseconds / 1000.0) * Mathf.Max(Time.timeScale, 0f), 0.12f);
				if (catchUp > 0f)
					navigator.transitionDriver.UpdateTransition(catchUp);
				_isTransitioning = navigator.transitionDriver?.GetTransition != null;
			}
		}

		private void ApplyNavigationStop(NavigatorTransitionPacket packet)
		{
			CancelTransition();
			ApplyBoundaryPosition(packet.SourcePosition);
			navigator.SetCurrentNavType(packet.StopNavType);

			if (packet.PlayIdle && navigator.NavGrid != null)
			{
				KAnimControllerBase_Patches.AllowAnims();
				try
				{
					animController.PlaySpeedMultiplier = 1f;
					animController.Play(navigator.NavGrid.GetIdleAnim(packet.StopNavType), KAnim.PlayMode.Loop);
				}
				finally
				{
					KAnimControllerBase_Patches.ForbidAnims();
				}
			}
		}

		private void UpdateActiveTransition()
		{
			if (!_isTransitioning || navigator.transitionDriver == null)
				return;

			try
			{
				navigator.transitionDriver.UpdateTransition(Time.deltaTime);
			}
			catch (Exception ex)
			{
				DebugConsole.LogWarning($"[DuplicantPlayback] Transition update failed on {gameObject.name}: {ex}");
				CancelTransition();
				return;
			}

			_isTransitioning = navigator.transitionDriver.GetTransition != null;
		}

		private void CancelTransition()
		{
			if (navigator?.transitionDriver?.GetTransition != null)
				navigator.transitionDriver.EndTransition();
			_isTransitioning = false;
		}

		private void ApplyBoundaryPosition(Vector3 serverPosition)
		{
			float error = Vector3.Distance(transform.position, serverPosition);
			if (error > HardCorrectionDistance)
				HardCorrections++;
			else if (error > SoftCorrectionDeadZone)
				SoftCorrections++;
			transform.SetPosition(serverPosition);
		}

		private void ApplyBufferedPosition(double playbackTime, bool isPaused)
		{
			if (_snapshots.Count == 0)
				return;

			ApplyDueTeleport(playbackTime);
			if (_snapshots.Count == 0)
				return;
			if (isPaused)
			{
				ApplyPausedPosition(playbackTime);
				return;
			}

			while (_snapshots.Count > 2 && _snapshots[1].LocalTimestamp <= playbackTime)
			{
				MarkSnapshotApplied(_snapshots[0].Snapshot.Sequence);
				_snapshots.RemoveAt(0);
			}

			var from = _snapshots[0];
			var stateSample = from;
			Vector3 desired;
			bool hasFreshPositionTarget = true;
			if (_snapshots.Count > 1 && playbackTime < _snapshots[1].LocalTimestamp)
			{
				var to = _snapshots[1];
				double duration = Math.Max(1.0, to.LocalTimestamp - from.LocalTimestamp);
				float t = Mathf.Clamp01((float)((playbackTime - from.LocalTimestamp) / duration));
				// Host navigation moves at constant speed inside a transition. SmoothStep
				// forced the velocity to zero at every 20 Hz sample boundary, producing a
				// visible accelerate/brake cycle. Linear interpolation preserves velocity.
				desired = Vector3.Lerp(from.Snapshot.Position, to.Snapshot.Position, t);
			}
			else
			{
				stateSample = _snapshots[_snapshots.Count - 1];
				desired = stateSample.Snapshot.Position;
				double ageMs = playbackTime - stateSample.LocalTimestamp;
				if (stateSample.Snapshot.IsMoving && _snapshots.Count > 1
					&& ageMs > 0.0 && ageMs <= MaxExtrapolationMs)
				{
					var previous = _snapshots[_snapshots.Count - 2];
					double sampleSeconds = Math.Max(0.001,
						(stateSample.LocalTimestamp - previous.LocalTimestamp) / 1000.0);
					Vector3 velocity = (stateSample.Snapshot.Position - previous.Snapshot.Position)
						/ (float)sampleSeconds;
					velocity = Vector3.ClampMagnitude(velocity, 20f);
					desired += velocity * (float)(ageMs / 1000.0);
					_underflowActive = false;
				}
				else if (stateSample.Snapshot.IsMoving && ageMs > MaxExtrapolationMs)
				{
					// A semantic ONI transition is a better short-term predictor than an old
					// position packet. Do not pull it backwards when snapshots briefly stall.
					hasFreshPositionTarget = !_isTransitioning;
					if (!_underflowActive)
					{
						BufferUnderflows++;
						_underflowActive = true;
					}
				}
				else
				{
					_underflowActive = false;
				}
			}

			ApplyFacingAndNavType(stateSample.Snapshot);
			if (stateSample.LocalTimestamp <= playbackTime)
				MarkSnapshotApplied(stateSample.Snapshot.Sequence);
			if (_isTransitioning && !hasFreshPositionTarget)
				return;
			float error = Vector3.Distance(transform.position, desired);
			if (_isTransitioning)
			{
				if (error > HardCorrectionDistance)
				{
					CancelTransition();
					transform.SetPosition(desired);
					HardCorrections++;
				}
				else if (error > SoftCorrectionDeadZone)
				{
					float correction = 1f - Mathf.Exp(-SoftCorrectionGain * Mathf.Max(Time.deltaTime, 0f));
					transform.SetPosition(Vector3.Lerp(transform.position, desired, correction));
					SoftCorrections++;
				}
			}
			else
			{
				transform.SetPosition(desired);
			}
		}

		private void ApplyPausedPosition(double playbackTime)
		{
			int dueIndex = -1;
			for (int i = 0; i < _snapshots.Count; i++)
			{
				if (_snapshots[i].LocalTimestamp > playbackTime)
					break;
				dueIndex = i;
			}
			if (dueIndex < 0)
				return;

			var latest = _snapshots[dueIndex];
			transform.SetPosition(latest.Snapshot.Position);
			ApplyFacingAndNavType(latest.Snapshot);
			MarkSnapshotApplied(latest.Snapshot.Sequence);
			if (dueIndex > 0)
				_snapshots.RemoveRange(0, dueIndex);
			_underflowActive = false;
		}

		private void ApplyDueTeleport(double playbackTime)
		{
			int teleportIndex = -1;
			for (int i = 0; i < _snapshots.Count; i++)
			{
				if (_snapshots[i].LocalTimestamp > playbackTime)
					break;
				if (_snapshots[i].Snapshot.IsTeleport)
					teleportIndex = i;
			}
			if (teleportIndex < 0)
				return;

			var teleport = _snapshots[teleportIndex];
			CancelTransition();
			_navigationEvents.RemoveAll(e => e.LocalTimestamp <= teleport.LocalTimestamp);
			transform.SetPosition(teleport.Snapshot.Position);
			ApplyFacingAndNavType(teleport.Snapshot);
			HardCorrections++;
			MarkSnapshotApplied(teleport.Snapshot.Sequence);

			teleport.Snapshot = new DuplicantVisualSnapshot
			{
				NetId = teleport.Snapshot.NetId,
				Sequence = teleport.Snapshot.Sequence,
				Position = teleport.Snapshot.Position,
				NavType = teleport.Snapshot.NavType,
				Flags = teleport.Snapshot.Flags & ~DuplicantVisualSnapshotFlags.Teleport,
			};
			if (teleportIndex > 0)
				_snapshots.RemoveRange(0, teleportIndex);
		}

		private void ApplyFacingAndNavType(DuplicantVisualSnapshot snapshot)
		{
			animController.FlipX = snapshot.FlipX;
			animController.FlipY = snapshot.FlipY;
			if (!_isTransitioning && navigator.CurrentNavType != snapshot.NavType)
				navigator.SetCurrentNavType(snapshot.NavType);
		}

		private void MarkSnapshotApplied(uint sequence)
		{
			if (sequence != 0 && IsNewerSequence(sequence, _lastAppliedSnapshotSequence))
				_lastAppliedSnapshotSequence = sequence;
		}

		private void ProcessAnimationEvents(double playbackTime)
		{
			if (_isTransitioning)
				return;

			while (_animationEvents.Count > 0 && _animationEvents[0].LocalTimestamp <= playbackTime)
			{
				var timed = _animationEvents[0];
				_animationEvents.RemoveAt(0);
				var packet = timed.Packet;
				if (!IsNewerSequence(packet.Sequence, _lastAppliedAnimationSequence))
				{
					DroppedStaleEvents++;
					continue;
				}

				ApplyAnimationEvent(packet, playbackTime - timed.LocalTimestamp);
				_lastAppliedAnimationSequence = packet.Sequence;
			}
		}

		private void ApplyAnimationEvent(PlayAnimPacket packet, double lateMilliseconds)
		{
			HashedString first = packet.AnimHashes[0];
			// A newer state keyframe already describes the animation visible at this
			// point on the shared timeline. Replaying an older non-queued Play event
			// would rewind the duplicant after a delayed reliable delivery.
			if (!packet.IsQueue && packet.TimeStamp <= _lastAppliedStateTimestamp)
			{
				return;
			}

			float lateSeconds = Mathf.Max(0f, (float)(lateMilliseconds / 1000.0))
				* Mathf.Max(Time.timeScale, 0f);
			float timeOffset = packet.TimeOffset + lateSeconds * Mathf.Max(packet.Speed, 0f);
			KAnimControllerBase_Patches.AllowAnims();
			try
			{
				if (packet.AnimHashes.Length > 1)
				{
					animController.Play(packet.AnimHashes, packet.Mode);
				}
				else if (packet.IsQueue)
				{
					animController.Queue(first, packet.Mode, packet.Speed, packet.TimeOffset);
				}
				else
				{
					animController.Play(first, packet.Mode, packet.Speed, timeOffset);
				}
			}
			finally
			{
				KAnimControllerBase_Patches.ForbidAnims();
			}
			AnimReconciliationHelper.ForceAnimUpdate(animController, nameof(PlayAnimPacket));
		}

		private void ProcessStateEvents(double playbackTime)
		{
			if (_isTransitioning || _stateEvents.Count == 0)
				return;

			int dueIndex = -1;
			for (int i = 0; i < _stateEvents.Count; i++)
			{
				if (_stateEvents[i].LocalTimestamp > playbackTime)
					break;
				dueIndex = i;
			}
			if (dueIndex < 0)
				return;

			var timed = _stateEvents[dueIndex];
			_stateEvents.RemoveRange(0, dueIndex + 1);
			var packet = timed.Packet;
			if (!IsNewerSequence(packet.Sequence, _lastAppliedStateSequence))
			{
				DroppedStaleEvents++;
				return;
			}

			ApplyAnimationState(packet, playbackTime - timed.LocalTimestamp);
			_lastAppliedStateSequence = packet.Sequence;
			_lastAppliedStateTimestamp = packet.ServerTimestamp;
		}

		private void ApplyAnimationState(DuplicantStatePacket packet, double lateMilliseconds)
		{
			if (packet.CurrentAnimHash == 0)
				return;

			var animHash = new HashedString(packet.CurrentAnimHash);
			var playMode = (KAnim.PlayMode)packet.AnimPlayMode;
			float targetElapsed = packet.AnimElapsedTime
				+ Mathf.Max(0f, (float)(lateMilliseconds / 1000.0))
					* Mathf.Max(Time.timeScale, 0f) * Mathf.Max(packet.AnimSpeed, 0f);
			if (animController.currentAnim != animHash)
			{
				KAnimControllerBase_Patches.AllowAnims();
				try
				{
					animController.Play(animHash, playMode, packet.AnimSpeed, 0f);
				}
				finally
				{
					KAnimControllerBase_Patches.ForbidAnims();
				}
				AnimReconciliationHelper.ForceAnimUpdate(animController, nameof(DuplicantStatePacket));
				AnimReconciliationHelper.TrySetElapsedTime(animController, targetElapsed);
				return;
			}

			float drift = CalculateAnimationDrift(targetElapsed, animController.GetElapsedTime(), playMode);
			float absDrift = Mathf.Abs(drift);
			if (absDrift > 1f)
			{
				AnimReconciliationHelper.TrySetElapsedTime(animController, targetElapsed);
				animController.PlaySpeedMultiplier = 1f;
				_animationSpeedAdjustmentUntil = 0f;
			}
			else if (absDrift > 0.08f)
			{
				animController.PlaySpeedMultiplier = Mathf.Clamp(1f + drift * 0.25f, 0.92f, 1.08f);
				_animationSpeedAdjustmentUntil = Time.unscaledTime + 0.5f;
			}
		}

		private float CalculateAnimationDrift(float target, float local, KAnim.PlayMode mode)
		{
			float drift = target - local;
			if (mode != KAnim.PlayMode.Loop)
				return drift;

			float duration = animController.GetDuration();
			if (duration <= 0.001f)
				return drift;
			return Mathf.Repeat(drift + duration * 0.5f, duration) - duration * 0.5f;
		}

		private void UpdateAnimationSpeedAdjustment()
		{
			if (_isTransitioning || _animationSpeedAdjustmentUntil <= 0f
				|| Time.unscaledTime < _animationSpeedAdjustmentUntil)
			{
				return;
			}

			animController.PlaySpeedMultiplier = 1f;
			_animationSpeedAdjustmentUntil = 0f;
		}

		private void InsertSnapshot(TimedSnapshot item)
		{
			int index = _snapshots.FindIndex(existing =>
				existing.LocalTimestamp > item.LocalTimestamp
				|| (existing.LocalTimestamp == item.LocalTimestamp
					&& IsNewerSequence(existing.Snapshot.Sequence, item.Snapshot.Sequence)));
			if (index < 0) _snapshots.Add(item);
			else _snapshots.Insert(index, item);
		}

		private void InsertNavigationEvent(TimedNavigationEvent item)
		{
			int index = _navigationEvents.FindIndex(existing =>
				existing.LocalTimestamp > item.LocalTimestamp
				|| (existing.LocalTimestamp == item.LocalTimestamp
					&& IsNewerSequence(existing.Packet.Sequence, item.Packet.Sequence)));
			if (index < 0) _navigationEvents.Add(item);
			else _navigationEvents.Insert(index, item);
		}

		private void InsertAnimationEvent(TimedAnimationEvent item)
		{
			int index = _animationEvents.FindIndex(existing =>
				existing.LocalTimestamp > item.LocalTimestamp
				|| (existing.LocalTimestamp == item.LocalTimestamp
					&& IsNewerSequence(existing.Packet.Sequence, item.Packet.Sequence)));
			if (index < 0) _animationEvents.Add(item);
			else _animationEvents.Insert(index, item);
		}

		private void InsertStateEvent(TimedStateEvent item)
		{
			int index = _stateEvents.FindIndex(existing =>
				existing.LocalTimestamp > item.LocalTimestamp
				|| (existing.LocalTimestamp == item.LocalTimestamp
					&& IsNewerSequence(existing.Packet.Sequence, item.Packet.Sequence)));
			if (index < 0) _stateEvents.Add(item);
			else _stateEvents.Insert(index, item);
		}

		internal static bool IsNewerSequence(uint incoming, uint current)
		{
			return incoming != current && unchecked(incoming - current) < 0x80000000u;
		}
	}
}
