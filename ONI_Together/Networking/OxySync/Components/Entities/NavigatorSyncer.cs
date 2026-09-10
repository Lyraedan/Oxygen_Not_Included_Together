using System.Collections.Generic;
using ONI_Together.DebugTools;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using Shared.Profiling;
using UnityEngine;
using System;

namespace ONI_Together.Networking.OxySync.Components.Entities
{
    [FixedInterestGroup]
	public class NavigatorSyncer : NetworkBehaviour
	{
        // Accelerate the movement speed on client side to compensate for network latency.
        // Ideally, this should be dynamically adjusted based on network conditions. but out of scope now.
        private const float BASE_SPEED_MULTIPLIER = 1.02f;

        private const float CATCHUP_PER_PENDING = 0.12f;
        private const float MAX_CATCHUP_MULTIPLIER = 1.45f;
        private const float MISSING_SEQUENCE_GRACE_SECONDS = 0.2f;
        private const float ACTIVE_TRANSITION_STUCK_SECONDS = 0.9f;
        private const float STUCK_POSITION_EPSILON = 0.01f;

        [Serializable]
        public sealed class Transition
        {
            public byte Id;
            public Vector2 StartPosition;
            public float Speed;
            public float AnimSpeed;
            public byte StartNavType;

            public override string ToString()
            {
                return $"Id={Id}, StartPosition={StartPosition}, Speed={Speed:F3}, AnimSpeed={AnimSpeed:F3}, StartNavType={(NavType)StartNavType}";
            }
        }

        [MyCmpGet]
        private Navigator navigator;

        private string EntityName { get {
            return gameObject?.GetProperName() ?? "";
        }}

        uint ServerNextSequence = 1;
        uint ClientNextSequence = 1;
        float LastClientSequenceAdvanceTime;
        float LastClientMovementTime;
        Vector3 LastClientPosition;
        SortedDictionary<uint, Transition> PendingTransitions = new();

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();

			base.OnSpawn();

            // When focusing an minions or creature, the interest group does not work as expected.
            // Packets would not be send to those clients watching the minion/creature.
            // We may change to use interest group in the future if the interest group is fixed.
            InterestGroup = -1;
			
            if (navigator == null)
            {
                gameObject?.TryGetComponent<Navigator>(out navigator);
            }

            if (navigator == null)
            {
                DebugConsole.LogError($"[NavigatorSyncer] Navigator is null on {gameObject?.GetProperName()}");
            }

            LastClientSequenceAdvanceTime = Time.unscaledTime;
            if (navigator != null)
                LastClientPosition = navigator.transform.position;
            LastClientMovementTime = Time.unscaledTime;
		}

		public override void OnCleanUp()
		{
			using var _ = Profiler.Scope();

			base.OnCleanUp();

            PendingTransitions.Clear();
		}

        public void RequestSyncTransition(bool isStop, Transition transition)
        {
            using var _ = Profiler.Scope();

            if (transition == null)
                return;
            try
            {
                float time = Time.unscaledTime;
                if (isStop)
                    CallClientRpc(nameof(RpcStopTransition), time, ServerNextSequence, transition.StartPosition, transition.StartNavType);
                else
                    CallClientRpc(nameof(RpcNextTransition), time, ServerNextSequence, transition);

                // Suppress the log until we have a flag to control it.
                // DebugConsole.LogSuccess($"[NavigatorSyncer] RequestSyncTransition called with sequence: {ServerNextSequence}, timestamp: {time}, name: {EntityName}");
                
                ServerNextSequence++;
            }
            catch (Exception e)
            {
                DebugConsole.LogError($"[NavigatorSyncer] Failed to request sync transition: {e}");
            }
        }

        [ClientRpc(SendMode = (int)PacketSendMode.UnreliableImmediate)]
		private void RpcNextTransition(float timestamp, uint sequence, Transition transition)
		{
			using var _ = Profiler.Scope();

            // Suppress the log until we have a flag to control it.
            // DebugConsole.LogSuccess($"[NavigatorSyncer][RpcNextTransition] sequence: {sequence}, timestamp: {timestamp}, name: {EntityName}, NetId: {NetId}");

			// We ignore stale transitions,
            // but we still want to keep track of the latest sequence number so we can prune pending transitions.
            if (sequence < ClientNextSequence)
                return;

            // Late-join/bootstrap case: if first sequence we ever see is not 1,
            // initialize expected sequence so pending dispatch can progress.
            if (ClientNextSequence == 1 && PendingTransitions.Count == 0 && sequence > 1)
            {
                ClientNextSequence = sequence;
                LastClientSequenceAdvanceTime = Time.unscaledTime;
            }

            if (!PendingTransitions.ContainsKey(sequence))
                PendingTransitions[sequence] = transition;

            // SimEveryTick runs only while moving. Kick dispatch immediately on receive
            // so stopped clients can start the first replicated transition.
            TryDispatchPending();
		}

		[ClientRpc(SendMode = (int)PacketSendMode.ReliableImmediate)]
		private void RpcStopTransition(float timestamp, uint sequence, Vector2 startPosition, byte endNavType)
		{
			using var _ = Profiler.Scope();

            // Suppress the log until we have a flag to control it.
            // DebugConsole.LogSuccess($"[NavigatorSyncer][RpcStopTransition] sequence: {sequence}, timestamp: {timestamp}, name: {EntityName}, NetId: {NetId}");

            // If it is stale, we ignore older stop transitions.
            if (sequence < ClientNextSequence)
                return;

            ClientNextSequence = sequence + 1;
            LastClientSequenceAdvanceTime = Time.unscaledTime;
            PrunePendingUpTo(sequence);
            SetPosition(startPosition);
            navigator.SetCurrentNavType((NavType)endNavType);
            navigator.Stop(arrived_at_destination: false, play_idle: true);
		}
	
		[Client]
		public void TryDispatchPending()
		{
			using var _ = Profiler.Scope();

			if (navigator == null)
            {
                DebugConsole.LogAssert($"[NavigatorSyncer] Navigator is null on {EntityName}");
                return;
            }

            if (!isClient)
                return;

            if (navigator.transitionDriver?.GetTransition != null)
            {
                // We are receiving transitions, but if the active transition never
                // completes locally, dispatch is permanently blocked and the entity freezes.
                // Recover by forcing transition teardown after a short stall window.
                if (PendingTransitions.Count > 0 && IsActiveTransitionStuck())
                {
                    DebugConsole.LogWarning(
                        $"[NavigatorSyncer] Recovering stuck transition on {EntityName} " +
                        $"(NetId={NetId}, pending={PendingTransitions.Count}, expectedSeq={ClientNextSequence})");

                    navigator.transitionDriver.EndTransition();
                    navigator.Stop(arrived_at_destination: false, play_idle: true);
                }

				return;
            }
            
            if (PendingTransitions.Count == 0)
                return;

            if (!PendingTransitions.ContainsKey(ClientNextSequence)
                && TryGetLowestPendingSequence(out var lowestPending)
                && lowestPending > ClientNextSequence
                && Time.unscaledTime - LastClientSequenceAdvanceTime >= MISSING_SEQUENCE_GRACE_SECONDS)
            {
                // Unreliable transition may be permanently lost. Jump to the next available
                // host-anchored transition so client movement can recover.
                ClientNextSequence = lowestPending;
                LastClientSequenceAdvanceTime = Time.unscaledTime;
            }
            
            while (PendingTransitions.TryGetValue(ClientNextSequence, out var transition))
            {
                PendingTransitions.Remove(ClientNextSequence);
                ClientNextSequence++;
                LastClientSequenceAdvanceTime = Time.unscaledTime;

                int pendingBacklog = PendingTransitions.Count;

				if (TryApplyBeginTransition(navigator, transition, pendingBacklog))
					break;
            }
		}

        [Client]
        private bool IsActiveTransitionStuck()
        {
            if (navigator == null)
                return false;

            float now = Time.unscaledTime;
            Vector3 currentPos = navigator.transform.position;
            if ((currentPos - LastClientPosition).sqrMagnitude > STUCK_POSITION_EPSILON * STUCK_POSITION_EPSILON)
            {
                LastClientPosition = currentPos;
                LastClientMovementTime = now;
                return false;
            }

            return now - LastClientMovementTime >= ACTIVE_TRANSITION_STUCK_SECONDS;
        }

        [Client]
        private bool TryGetLowestPendingSequence(out uint sequence)
        {
            foreach (var key in PendingTransitions.Keys)
            {
                sequence = key;
                return true;
            }

            sequence = 0;
            return false;
        }

        [Client]
        private void PrunePendingUpTo(uint sequence)
        {
            if (PendingTransitions.Count == 0)
                return;

            List<uint> toRemove = null;
            foreach (var key in PendingTransitions.Keys)
            {
                if (key > sequence)
                    break;

                toRemove ??= new List<uint>();
                toRemove.Add(key);
            }

            if (toRemove == null)
                return;

            for (int i = 0; i < toRemove.Count; i++)
                PendingTransitions.Remove(toRemove[i]);
        }

        [Client]
        private bool TryApplyBeginTransition(Navigator navigator, Transition transition, int pendingBacklog)
        {
            using var _ = Profiler.Scope();

            if (navigator.NavGrid == null || navigator.NavGrid.transitions == null)
				return false;
            
            int index = transition.Id;
            if (index < 0 || index >= navigator.NavGrid.transitions.Length)
                return false;
            
            var currentTransition = navigator.NavGrid.transitions[index];
            // Snap to host anchor before replaying the next transition so drift does not
			// accumulate into wrong stop cells or wall-climb offsets.
            SetPosition(transition.StartPosition);
            navigator.SetCurrentNavType((NavType)transition.StartNavType);
			navigator.BeginTransition(currentTransition);

            // Apply host-resolved transition speeds after transition start so client movement rate matches authority.
			var activeTransition = navigator.transitionDriver?.GetTransition;
			if (activeTransition != null)
			{
                float catchupMultiplier = BASE_SPEED_MULTIPLIER + Mathf.Min(pendingBacklog * CATCHUP_PER_PENDING, MAX_CATCHUP_MULTIPLIER - 1f);
                float speed = transition.Speed * catchupMultiplier;
                float animSpeed = transition.AnimSpeed * catchupMultiplier;

                activeTransition.speed = speed;
                activeTransition.animSpeed = animSpeed;
                navigator.animController?.PlaySpeedMultiplier = animSpeed;
			}

			return true;
        }

		[Client]
		private void SetPosition(Vector2 pos)
		{
			using var _ = Profiler.Scope();

            if (navigator == null)
            {
                DebugConsole.LogAssert($"[NavigatorSyncer] Navigator is null on {gameObject?.GetProperName()}");
                return;
            }

			try
			{
				var currentPos = navigator.transform.position;
				navigator.transform.SetPosition(new Vector3(pos.x, pos.y, currentPos.z));
			}
			catch (System.Exception e)
			{
				DebugConsole.LogError(
                    $"[NavigatorSyncer] Failed to set position "+
                    $"on {navigator.gameObject?.GetProperName()}: {e}");
			}

		}

		private void Update()
		{
            // Fallback dispatch only when not moving. While moving, SimEveryTick
            // patch is the primary dispatch path to align with sim timing.
            if (!isClient || PendingTransitions.Count == 0 || navigator == null)
                return;

            if (navigator.IsMoving())
                return;

            TryDispatchPending();

			return;
		}
	}
}
