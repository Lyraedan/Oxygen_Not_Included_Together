using System.Linq;
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
	public class AnimSyncer : NetworkBehaviour
	{
		[Serializable]
		private sealed class AnimRequest
		{
			public bool Queueing;
			public HashedString[] AnimNames;
			public KAnim.PlayMode Mode;
			public float Speed;
			public float TimeOffset;
			[NonSerialized]
			public bool IsLocomotion;
		}

		[MyCmpGet]
		private KBatchedAnimController animController;
		[MyCmpGet]
		private Navigator navigator;
		[MyCmpGet]
		private NavigatorSyncer navigatorSyncer;

		private string EntityName => gameObject.GetProperName();

		private uint NextSequence = 1;
		private uint ExpectedSequence = 1;
		private float MissingSequenceSince = -1f;
		private HashedString LastTransitionAnim = default;
		private HashedString LastTransitionPreAnim = default;
		private float LastTransitionSeenAt = -1f;

		// Keep skip enabled for recovery, but be conservative because RpcPlayAnim is reliable.
		private const float MISSING_SEQUENCE_GRACE_SECONDS = 0.75f;
		private const float TRANSITION_HANDOFF_WINDOW_SECONDS = 1.5f;

		private readonly SortedDictionary<uint, AnimRequest> PendingAnims = new();

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();

			base.OnSpawn();

			// For the same reason as NavigatorSyncer,
			// we need to set the interest group to -1 to sync anim to those clients actually watching this entity.
			InterestGroup = -1;

			NextSequence = 1;
			ExpectedSequence = 1;
			LastTransitionAnim = default;
			LastTransitionPreAnim = default;
			LastTransitionSeenAt = -1f;
			PendingAnims.Clear();
		}


		public override void OnCleanUp()
		{
			using var _ = Profiler.Scope();

			PendingAnims.Clear();
			LastTransitionAnim = default;
			LastTransitionPreAnim = default;
			LastTransitionSeenAt = -1f;
			base.OnCleanUp();
		}

		public void RequestToPlayAnim(bool queueing, HashedString[] animNames, KAnim.PlayMode mode, float speed = 1f, float timeOffset = 0f)
		{
			using var _ = Profiler.Scope();
			if (!isServer || !MultiplayerSession.SessionHasPlayers)
			{
				return;
			}

			if (animNames == null || animNames.Length == 0 || animNames[0] == default)
				return;

			// Locomotion animations will be handled by the clients' transition,
			// so we don't want to send them through the AnimSyncer.
			if (ShouldIgnoreLocomotionFromAnimSyncer(animNames))
				return;

			try
			{
				AnimRequest request = new AnimRequest
				{
					Queueing = queueing,
					AnimNames = animNames,
					Mode = mode,
					Speed = speed,
					TimeOffset = timeOffset
				};
				CallClientRpc(nameof(RpcPlayAnim), NextSequence, request);

				string animName = ResolveAnimName(animNames.FirstOrDefault());
				DebugConsole.LogSuccess($"[AnimSyncer] Sent animation packet: {NextSequence} for {EntityName} with NetId {NetId}: {animName}");

				NextSequence++;

			}
			catch (Exception e)
			{
				DebugConsole.LogError($"[AnimSyncer] Failed to send animation packet to {EntityName} with NetId {NetId}: {e}");
			}
		}

		[ClientRpc(SendMode = (int)PacketSendMode.Reliable)]
		private void RpcPlayAnim(uint sequence, AnimRequest request)
		{
			using var _ = Profiler.Scope();

			if (!isClient)
				return;

			string animName = ResolveAnimName(request?.AnimNames?.FirstOrDefault() ?? default);
			DebugConsole.LogSuccess($"[AnimSyncer] Received animation packet: {sequence} for {EntityName} with NetId {NetId}: {animName}");

			if (request == null || request.AnimNames == null || request.AnimNames.Length == 0 || request.AnimNames[0] == default)
				return;

			if (sequence != 0 && sequence < ExpectedSequence)
				return;

			// If the first packet received is not the first sequence, we will accept it and set the expected sequence to it.
			if (ExpectedSequence == 1 && PendingAnims.Count == 0 && sequence > 1)
			{
				ExpectedSequence = sequence;
			}

			if (!PendingAnims.ContainsKey(sequence))
			{
				PendingAnims[sequence] = request;
				PendingAnims[sequence].IsLocomotion = IsCurrentNavigatorTransitionAnim(request.AnimNames[0]);
			}

			TryDispatchPending();
		}

		// This is the lock to allow animations to be played in a synchronized manner on the client.
		private int allowPlaybackDepth = 0;
		public void EnterSyncedPlaybackScope() => allowPlaybackDepth++;
		public void ExitSyncedPlaybackScope()
		{
			if (allowPlaybackDepth > 0)
				allowPlaybackDepth--;
		}
		public bool IsInSyncedPlaybackScope() => allowPlaybackDepth > 0;

		// This is the lock to allow animations to be overridden in a synchronized manner on the client.
		private int allowOverrideDepth = 0;
		public void EnterOverrideScope() => allowOverrideDepth++;
		public void ExitOverrideScope()
		{
			if (allowOverrideDepth > 0)
				allowOverrideDepth--;
		}
		public bool IsInOverrideScope() => allowOverrideDepth > 0;
	
		[Client]
		public void PlayAnim(bool queueing, HashedString[] animNames, KAnim.PlayMode mode, float speed = 1f, float timeOffset = 0f, bool isSync = false, bool forceUpdate = true)
		{
			using var _ = Profiler.Scope();

			if (!CanSyncAnim(animNames, out var kbac))
				return;

			if (ShouldIgnoreLocomotionFromAnimSyncer(animNames))
				return;

			try
			{
				EnterSyncedPlaybackScope();

				HashedString primaryAnim = animNames.FirstOrDefault();
				bool forcePlayAfterTransition = queueing
					&& animNames.Length == 1
					&& !IsMovementActive()
					&& IsRecentNavigatorTransitionAnim(kbac.currentAnim);

				if (animNames.Length > 1)
					kbac.Play(animNames, mode);
				else if (forcePlayAfterTransition)
					kbac.Play(primaryAnim, mode, speed, timeOffset);
				else if (queueing)
					kbac.Queue(primaryAnim, mode, speed, timeOffset);
				else if (!isSync)
					kbac.Play(primaryAnim, mode, speed, timeOffset);
				else if (kbac.currentAnim != primaryAnim)
					kbac.Play(primaryAnim, mode, speed, 0f);

				if (forceUpdate)
					ForceAnimUpdate(kbac);

				if (isSync)
				{
					kbac.SetElapsedTime(timeOffset);
				}

				string animName = ResolveAnimName(animNames.FirstOrDefault());
				DebugConsole.LogSuccess($"[AnimSyncer] played animation for {EntityName} with NetId {NetId}: {animName}");
			}
			catch (Exception e)
			{
				DebugConsole.LogError($"[AnimSyncer] Failed to play animation for {EntityName} with NetId {NetId}: {e}");
			}
			finally
			{
				ExitSyncedPlaybackScope();
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
			catch (Exception e)
			{
				DebugConsole.LogError($"[AnimSyncer] Failed to force animation update for {EntityName} with NetId {NetId}: {e}");
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

		private bool IsMovementActive()
		{
			if (navigator == null)
				navigator = GetComponent<Navigator>();

			if (navigator == null)
				return false;

			if (navigator.transitionDriver?.GetTransition != null)
				return true;

			return navigator.IsMoving();
		}

		private bool IsCurrentNavigatorTransitionAnim(HashedString animName)
		{
			if (animName == default)
				return false;

			if (navigator == null)
				navigator = GetComponent<Navigator>();

			var activeTransition = navigator?.transitionDriver?.GetTransition;
			if (activeTransition == null)
				return false;

			LastTransitionAnim = activeTransition.anim;
			LastTransitionPreAnim = activeTransition.preAnim;
			LastTransitionSeenAt = Time.unscaledTime;

			return animName == activeTransition.anim || animName == activeTransition.preAnim;
		}

		private bool IsRecentNavigatorTransitionAnim(HashedString animName)
		{
			if (animName == default || LastTransitionSeenAt < 0f)
				return false;

			if (Time.unscaledTime - LastTransitionSeenAt > TRANSITION_HANDOFF_WINDOW_SECONDS)
				return false;

			return animName == LastTransitionAnim || animName == LastTransitionPreAnim;
		}

		private bool IsCurrentOrRecentNavigatorTransitionAnim(HashedString animName)
		{
			return IsCurrentNavigatorTransitionAnim(animName) || IsRecentNavigatorTransitionAnim(animName);
		}

		[Client]
		private bool ShouldIgnoreLocomotionFromAnimSyncer(HashedString[] animNames)
		{
			if (animNames == null || animNames.Length == 0 || animNames[0] == default)
				return true;

			if (navigatorSyncer == null)
				navigatorSyncer = GetComponent<NavigatorSyncer>();

			if (navigatorSyncer == null)
				return false;

			HashedString primaryAnim = animNames.FirstOrDefault();
			if (IsCurrentOrRecentNavigatorTransitionAnim(primaryAnim))
			{
				string transitionAnimName = ResolveAnimName(primaryAnim);
				DebugConsole.LogNonImportant($"[AnimSyncer] Ignoring navigator transition animation for {EntityName} with NetId {NetId}: {transitionAnimName}");
				return true;
			}

			return false;
		}

		private string ResolveAnimName(HashedString animName)
		{
			if (animName == default)
				return string.Empty;

			if (animController != null)
			{
				var anim = animController.GetAnim(animName);
				if (anim != null && !string.IsNullOrEmpty(anim.name))
					return anim.name;
			}

			return animName.ToString();
		}

		[Client]
		private void TryDispatchPending()
		{
			if (!isClient || PendingAnims.Count == 0)
				return;

			if (IsMovementActive())
				return;

			var lowestPendingSequence = PendingAnims.Keys.Min();
			if (!PendingAnims.ContainsKey(ExpectedSequence) && lowestPendingSequence > ExpectedSequence)
			{
				if (MissingSequenceSince < 0f)
					MissingSequenceSince = Time.unscaledTime;

				// Slight adaptive grace avoids aggressive drops when queue is building.
				float adaptiveGrace = MISSING_SEQUENCE_GRACE_SECONDS + Mathf.Min(0.35f, PendingAnims.Count * 0.03f);
				if (Time.unscaledTime - MissingSequenceSince >= adaptiveGrace)
				{
					ExpectedSequence = lowestPendingSequence;
					MissingSequenceSince = -1f;
					DebugConsole.LogWarning($"[AnimSyncer] Skipping missing sequence(s) on {EntityName} (NetId={NetId}). Jumping to {ExpectedSequence}. Pending={PendingAnims.Count}");
				}
			}
			else
			{
				MissingSequenceSince = -1f;
			}

			while (PendingAnims.TryGetValue(ExpectedSequence, out var pending))
			{
				uint currentSequence = ExpectedSequence;

				if (IsMovementActive())
					break;

				PendingAnims.Remove(ExpectedSequence);
				ExpectedSequence++;

				if (pending.IsLocomotion)
					continue;
			

				PlayAnim(pending.Queueing, pending.AnimNames, pending.Mode, pending.Speed, pending.TimeOffset, false, true);
			}

			if (PendingAnims.Count == 0)
				MissingSequenceSince = -1f;
		}

		private void Update()
		{
			using var _ = Profiler.Scope();
			if (isClient && navigator != null)
				IsCurrentNavigatorTransitionAnim(navigator.transitionDriver?.GetTransition?.anim ?? default);
			if (isClient)
				TryDispatchPending();
		}
	}
}
