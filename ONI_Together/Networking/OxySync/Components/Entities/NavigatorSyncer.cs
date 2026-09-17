using System.Collections.Generic;
using ONI_Together.DebugTools;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using Shared.Profiling;
using UnityEngine;
using System;
using System.Linq;

namespace ONI_Together.Networking.OxySync.Components.Entities
{
    [FixedInterestGroup]
	public class NavigatorSyncer : NetworkBehaviour
	{
        private const bool ENABLE_LOG = true;

        // Accelerate the movement speed on client side to compensate for network latency.
        // Ideally, this should be dynamically adjusted based on network conditions. but out of scope now.
        private const float BASE_SPEED_MULTIPLIER = 1.02f;

        private const float CATCHUP_PER_PENDING = 0.12f;
        private const float MAX_CATCHUP_MULTIPLIER = 1.45f;
        private const float MISSING_SEQUENCE_GRACE_SECONDS = 0.2f;
        private const float ACTIVE_TRANSITION_STUCK_SECONDS = 0.4f;
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

        [MyCmpGet]
        private KBatchedAnimController animController;

        private string EntityName => gameObject?.GetProperName() ?? "Unknown Entity";

        uint ServerNextSequence = 1;
        uint ClientNextSequence = 1;
        float LastClientSequenceAdvanceTime;
        float LastClientMovementTime;
        Vector3 LastClientPosition;
        readonly SortedDictionary<uint, Transition> PendingTransitions = new();

		public override void OnPrefabInit()
		{
			base.OnPrefabInit();

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

            PendingTransitions.Clear();

			base.OnCleanUp();
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

                if (ENABLE_LOG)
                    DebugConsole.LogSuccess($"[NavigatorSyncer][SEND_TRANSITION]{EntityName}:{NetId} sequence: {ServerNextSequence}, timestamp: {time}");
                
                ServerNextSequence++;
            }
            catch (Exception e)
            {
                DebugConsole.LogError($"[NavigatorSyncer][SEND_TRANSITION]{EntityName}:{NetId} Failed to request sync transition: {e}");
            }
        }

        [ClientRpc(SendMode = (int)PacketSendMode.UnreliableImmediate)]
		private void RpcNextTransition(float timestamp, uint sequence, Transition transition)
		{
			using var _ = Profiler.Scope();

            if (ENABLE_LOG)
                DebugConsole.LogSuccess($"[NavigatorSyncer][RECEIVE_TRANSITION]{EntityName}:{NetId} sequence: {sequence}, timestamp: {timestamp}");

			// We ignore stale transitions,
            // but we still want to keep track of the latest sequence number so we can prune pending transitions.
            if (sequence < ClientNextSequence)
            {
                if (ENABLE_LOG)
                    DebugConsole.LogWarning($"[NavigatorSyncer][RECEIVE_TRANSITION]{EntityName}:{NetId} sequence: {sequence}, timestamp: {timestamp} is stale, ignoring");
                return;
            }

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

            if (ENABLE_LOG)
                DebugConsole.LogSuccess($"[NavigatorSyncer][RECEIVE_TRANSITION]{EntityName}:{NetId} sequence: {sequence}, timestamp: {timestamp}");

            // If it is stale, we ignore older stop transitions.
            if (sequence < ClientNextSequence)
            {
                if (ENABLE_LOG)
                    DebugConsole.LogWarning($"[NavigatorSyncer][RECEIVE_TRANSITION]{EntityName}:{NetId} sequence: {sequence}, timestamp: {timestamp} is stale, ignoring");
                return;
            }

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
                DebugConsole.LogAssert(
                    $"[NavigatorSyncer]{EntityName}:{NetId} " +
                    $"Navigator is null while trying to dispatch pending transitions");
                return;
            }
            
            if (PendingTransitions.Count == 0)
                return;

            // Check if the next expected sequence is missing and attempt to recover if necessary.
            var lowestPending = PendingTransitions.First().Key;
            if (!PendingTransitions.ContainsKey(ClientNextSequence)
                && lowestPending > ClientNextSequence
                && Time.unscaledTime - LastClientSequenceAdvanceTime >= MISSING_SEQUENCE_GRACE_SECONDS)
            {
                if (ENABLE_LOG)
                    DebugConsole.LogWarning(
                        $"[NavigatorSyncer]{EntityName}:{NetId} Missing sequence {ClientNextSequence}, jumping to {lowestPending}");

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
                if (navigator.animController != null)
                    navigator.animController.PlaySpeedMultiplier = animSpeed;
			}

			return true;
        }

		[Client]
		private void SetPosition(Vector2 pos)
		{
			using var _ = Profiler.Scope();

            if (navigator == null)
            {
                DebugConsole.LogAssert($"[NavigatorSyncer]{EntityName}:{NetId} Navigator is null.");
                return;
            }

			try
			{
				var currentPos = navigator.transform.position;
				navigator.transform.SetPosition(new Vector3(pos.x, pos.y, currentPos.z));
			}
			catch (Exception e)
			{
				DebugConsole.LogError($"[NavigatorSyncer]{EntityName}:{NetId} Failed to set position. {e}");
			}
		}

		private void Update()
		{
            if (!isClient || navigator == null)
                return;

            // Fallback dispatch only when not moving. While moving, SimEveryTick
            // patch is the primary dispatch path to align with sim timing.
            if (navigator.IsMoving() || PendingTransitions.Count == 0)
                return;

            TryDispatchPending();
		}
	}
}
