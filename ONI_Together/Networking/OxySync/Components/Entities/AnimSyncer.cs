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
		private const bool ENABLE_LOG = false;

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

		[Serializable]
		private sealed class SymbolVisibility
		{
			public KAnimHashedString Symbol;
			public bool IsVisible;
		}

		[MyCmpGet]
		private KBatchedAnimController animController;
		[MyCmpGet]
		private Navigator navigator;

		[SyncVar(Hook = nameof(OnSymbolVisibilityChanged))]
		SymbolVisibility Symbol;

		public string EntityName => gameObject?.GetProperName() ?? "Unknown Entity";

		private uint NextSequence = 1;
		private uint ExpectedSequence = 1;
		private float MissingSequenceSince = -1f;

		private const float MISSING_SEQUENCE_GRACE_SECONDS = 0.75f;

		private readonly SortedDictionary<uint, AnimRequest> PendingAnims = [];

		public override void OnPrefabInit()
		{
			base.OnPrefabInit();

			// For the same reason as NavigatorSyncer,
			// we need to set the interest group to -1 to sync anim to those clients actually watching this entity.
			InterestGroup = -1;

			NextSequence = 1;
			ExpectedSequence = 1;
			PendingAnims.Clear();
		}

		public override void OnCleanUp()
		{
			PendingAnims.Clear();
			ExpectedSequence = 1;
			NextSequence = 1;
			base.OnCleanUp();
		}

		public void RequestToPlayAnim(bool queueing, HashedString[] animNames, KAnim.PlayMode mode, float speed = 1f, float timeOffset = 0f)
		{
			using var _ = Profiler.Scope();
			if (!isServer || !MultiplayerSession.SessionHasPlayers)
				return;

			if (animNames == null || animNames.Length == 0 || animNames[0] == default)
				return;

			// Locomotion animations will be handled by the clients' transition,
			// so we don't want to send them through the AnimSyncer.
			if (IsNavigatorAnim(animNames.FirstOrDefault()))
			{
				if (ENABLE_LOG)
					DebugConsole.LogNonImportant($"[AnimSyncer][SEND_ANIM_SKIP_NAV]{EntityName}:{NetId} anim: {ResolveAnimName(animNames.FirstOrDefault())}");
				return;
			}

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

				if (ENABLE_LOG)
				{
					string animName = ResolveAnimName(animNames.FirstOrDefault());
					DebugConsole.LogSuccess($"[AnimSyncer][SEND_ANIM]{EntityName}:{NetId} seq: {NextSequence} anim: {animName}");
				}

				NextSequence++;

			}
			catch (Exception e)
			{
				DebugConsole.LogError($"[AnimSyncer][SEND_ANIM]{EntityName}:{NetId} Failed to send animation packet. {e}");
			}
		}

		public void RequestUpdateKanimOverride(string kanim_name, bool isAdding, float priority = 0f)
		{
			using var _ = Profiler.Scope();
			if (!isServer || !MultiplayerSession.SessionHasPlayers)
				return;

			if (string.IsNullOrEmpty(kanim_name))
				return;
			
			try
			{
				CallClientRpc(nameof(RpcUpdateKAnimOverrides), kanim_name, isAdding, priority);
				if (ENABLE_LOG)
					DebugConsole.LogSuccess($"[AnimSyncer][SEND_OVERRIDE]{EntityName}:{NetId} {(isAdding ? "Add" : "Remove")} {kanim_name}");
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[AnimSyncer][SEND_OVERRIDE]{EntityName}:{NetId} Failed to send kanim override update {kanim_name}. {ex}");
			}
		}

		public void RequestSymbolVisibilityChange(KAnimHashedString symbol, bool isVisible)
		{
			using var _ = Profiler.Scope();
			if (!isServer || !MultiplayerSession.SessionHasPlayers)
				return;

			Symbol = new SymbolVisibility
			{
				Symbol = symbol,
				IsVisible = isVisible
			};

			if (ENABLE_LOG)
				DebugConsole.LogNonImportant(
					$"[AnimSyncer][SymbolVisibility]{EntityName}:{NetId} " +
					$"Hash: {symbol} visiable: {isVisible} sent");
		}

		[ClientRpc(SendMode = (int)PacketSendMode.Reliable)]
		private void RpcPlayAnim(uint sequence, AnimRequest request)
		{
			using var _ = Profiler.Scope();

			if (!isClient)
				return;

			string animName = ResolveAnimName(request?.AnimNames?.FirstOrDefault() ?? default);
			if (ENABLE_LOG)
				DebugConsole.LogSuccess($"[AnimSyncer][RECEIVE_ANIM]{EntityName}:{NetId} seq: {sequence} anim: {animName}");

			if (request == null || request.AnimNames == null || request.AnimNames.Length == 0 || request.AnimNames[0] == default)
				return;

			if (sequence != 0 && sequence < ExpectedSequence)
			{
				DebugConsole.LogWarning($"[AnimSyncer][RECEIVE_ANIM_STALE]{EntityName}:{NetId} seq: {sequence} expected: {ExpectedSequence} anim: {animName}");
				return;
			}

			// If the first packet received is not the first sequence, we will accept it and set the expected sequence to it.
			if (ExpectedSequence == 1 && PendingAnims.Count == 0 && sequence > 1)
				ExpectedSequence = sequence;
			
			if (!PendingAnims.ContainsKey(sequence))
			{
				PendingAnims[sequence] = request;
				PendingAnims[sequence].IsLocomotion = IsNavigatorAnim(request.AnimNames[0]);
			}

			TryDispatchPending();
		}

		[ClientRpc(SendMode = (int)PacketSendMode.Reliable)]
		private void RpcUpdateKAnimOverrides(string kanim_name, bool isAdding, float priority)
		{
			using var _ = Profiler.Scope();

			if (!isClient)
				return;

			try
			{
				if (!Assets.TryGetAnim(kanim_name, out var anim) || anim == null)
				{
					DebugConsole.LogWarning($"[AnimSyncer][RECEIVE_OVERRIDE]{EntityName}:{NetId} Could not find anim {kanim_name}");
					return;
				}

				EnterOverrideScope();
				if (ENABLE_LOG)
					DebugConsole.LogNonImportant($"[AnimSyncer][RECEIVE_OVERRIDE]{EntityName}:{NetId} {(isAdding ? "Add" : "Remove")} {kanim_name}");
		
				if (isAdding)
					animController?.AddAnimOverrides(anim, priority);
				else
					animController?.RemoveAnimOverrides(anim);
			}
			catch (Exception e)
			{
				DebugConsole.LogError($"[AnimSyncer][RECEIVE_OVERRIDE]{EntityName}:{NetId} Failed to process kanim override. {e}");
			}
			finally
			{
				ExitOverrideScope();
			}
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

			string animName = ResolveAnimName(animNames.FirstOrDefault());

			try
			{
				EnterSyncedPlaybackScope();

				HashedString primaryAnim = animNames.FirstOrDefault();

				if (animNames.Length > 1)
					kbac.Play(animNames, mode);
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

				if (ENABLE_LOG)
					DebugConsole.LogSuccess($"[AnimSyncer][PLAY_ANIM]{EntityName}:{NetId} played {animName}.");
			}
			catch (Exception e)
			{
				DebugConsole.LogError($"[AnimSyncer][PLAY_ANIM]{EntityName}:{NetId} Failed to play animate {animName}. {e}");
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
				DebugConsole.LogError($"[AnimSyncer][FORCE_ANIM_UPDATE]{EntityName}:{NetId} Failed to force animation update. {e}");
			}

		}

		// Check if the animation can be synced and get the KBatchedAnimController
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

			// animates created by the navigator, it should be controlled by the navigator.
			if (IsNavigatorAnim(animNames.FirstOrDefault()))
				return false;
			
			if (animNames.Length == 1 && animNames[0] == animController.currentAnim)
				return false;

			kbc = animController;
			return true;
		}

		public bool IsNavigatorAnim(HashedString animName)
		{
			using var _ = Profiler.Scope();

			if (navigator == null)
				navigator = GetComponent<Navigator>();

			if (navigator == null || animName == null || animName == default)
				return false;

			// Check if the current animation is the idle animation for the navigator.
			if (navigator.NavGrid != null && navigator.NavGrid.GetIdleAnim(navigator.CurrentNavType) == animName)
				return true;

			var activeTransition = navigator?.transitionDriver?.GetTransition;
			if (activeTransition == null)
				return false;
			
			// Check if the current animation is part of an active transition.
			return animName == activeTransition.anim || animName == activeTransition.preAnim;
		}

		public string ResolveAnimName(HashedString animName)
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

			// Wait for the right anim for a short period. Some anim should play before the other.
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
				PendingAnims.Remove(ExpectedSequence);
				ExpectedSequence++;

				if (pending.IsLocomotion)
				{
					string animName = ResolveAnimName(pending.AnimNames.FirstOrDefault());
					DebugConsole.Log($"[AnimSyncer]{EntityName}:{NetId} Drop {animName}:{pending.AnimNames.FirstOrDefault()} because it is a locomotion animation");
					continue;
				}
			
				PlayAnim(pending.Queueing, pending.AnimNames, pending.Mode, pending.Speed, pending.TimeOffset, false, true);
			}

			if (PendingAnims.Count == 0)
				MissingSequenceSince = -1f;
		}

		[Client]
		private void OnSymbolVisibilityChanged(SymbolVisibility oldValue, SymbolVisibility newValue)
		{
			if (ENABLE_LOG)
				DebugConsole.LogNonImportant(
					$"[AnimSyncer][SymbolVisibility]{EntityName}:{NetId} " +
					$"Hash: {newValue.Symbol} visiable: {newValue.IsVisible} received");

			// Caution: the typo may be fixed in future game updates, but hope they would not.
			animController?.SetSymbolVisiblity(newValue.Symbol, newValue.IsVisible);
		}

		private void Update()
		{
			using var _ = Profiler.Scope();
			if (!isClient || navigator == null)
				return;

			if (!navigator.IsMoving())
				TryDispatchPending();
		}
	}
}
