using ONI_Together.DebugTools;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using Shared.Profiling;
using System;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components.Entities
{
	[SkipSaveFileSerialization]
	[FixedInterestGroup]
	public class AnimSyncer : NetworkBehaviour
	{
		private static readonly bool ENABLE_LOG = false;

		[Serializable]
		private sealed class AnimRequest
		{
			public bool Queueing;
			public HashedString[] AnimNames;
			public KAnim.PlayMode Mode;
			public float Speed;
			public float TimeOffset;
		}

		[Serializable]
		private sealed class AnimFlip
		{
			public bool FlipX;
			public bool FlipY;
		}

		[MyCmpGet]
		private KBatchedAnimController animController;

		public string EntityName => gameObject?.GetProperName() ?? "Unknown Entity";

		public override void OnPrefabInit()
		{
			base.OnPrefabInit();

			// For the same reason as NavigatorSyncer,
			// we need to set the interest group to -1 to sync anim to those clients actually watching this entity.
			InterestGroup = -1;
		}

		public void RequestToPlayAnim(
			bool queueing, HashedString[] animNames, KAnim.PlayMode mode, float speed = 1f, float timeOffset = 0f)
		{
			using var _ = Profiler.Scope();
			if (!isServer || !MultiplayerSession.SessionHasPlayers)
				return;

			if (animNames == null || animNames.Length == 0 || animNames[0] == default)
				return;

			try
			{
				AnimRequest request = new AnimRequest
				{
					Queueing = queueing,
					AnimNames = animNames,
					Mode = mode,
					Speed = speed,
					TimeOffset = timeOffset,
				};

				AnimFlip flip = new AnimFlip
				{
					FlipX = animController?.FlipX ?? false,
					FlipY = animController?.FlipY ?? false,
				};
				CallClientRpc(nameof(RpcPlayAnim), request, flip);
			}
			catch (Exception e)
			{
				DebugConsole.LogError(
					$"[AnimSyncer][SEND_ANIM]{EntityName}:{NetId} Failed to send animation packet. {e}");
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
			}
			catch (Exception ex)
			{
				DebugConsole.LogError(
					$"[AnimSyncer][SEND_OVERRIDE]{EntityName}:{NetId} " +
					$"Failed to send kanim override update {kanim_name}. {ex}");
			}
		}

		public void RequestSetSymbolVisiblity(KAnimHashedString symbol, bool isVisible)
		{
			using var _ = Profiler.Scope();
			if (!isServer || !MultiplayerSession.SessionHasPlayers)
				return;

			if (ENABLE_LOG)
				DebugConsole.LogNonImportant(
					$"[AnimSyncer][SYMBOL][SERVER]{EntityName}:{NetId} {symbol} visible: {isVisible}");

			try
			{
				CallClientRpc(nameof(RpcSetSymbolVisiblity), symbol, isVisible);
			}
			catch (Exception ex)
			{
				DebugConsole.LogError(
					$"[AnimSyncer][SYMBOL][SERVER]{EntityName}:{NetId} Failed to send symbol visibility change. {ex}");
			}
		}

		[ClientRpc(SendMode = (int)PacketSendMode.Reliable)]
		private void RpcPlayAnim(AnimRequest request, AnimFlip flip)
		{
			using var _ = Profiler.Scope();

			var animNames = request.AnimNames;

			if (!isClient || animNames == null || animNames.Length == 0 || animNames[0] == default)
				return;

			animController ??= GetComponent<KBatchedAnimController>();
			if (animController == null || (animNames.Length == 1 && animNames[0] == animController.currentAnim))
				return;

			animController.FlipX = flip.FlipX;
			animController.FlipY = flip.FlipY;

			try
			{
				EnterSyncedPlaybackScope();

				if (animNames.Length > 1)
					animController.Play(animNames, request.Mode);
				else if (request.Queueing)
					animController.Queue(animNames[0], request.Mode, request.Speed, request.TimeOffset);
				else
					animController.Play(animNames[0], request.Mode, request.Speed, request.TimeOffset);
			}
			catch (Exception e)
			{
				DebugConsole.LogError(
					$"[AnimSyncer][PLAY_ANIM] {EntityName}:{NetId} Failed to play animation {animNames[0]}. {e}");
			}
			finally
			{
				ExitSyncedPlaybackScope();
			}
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
					DebugConsole.LogWarning(
						$"[AnimSyncer][RECEIVE_OVERRIDE]{EntityName}:{NetId} Could not find anim {kanim_name}");
					return;
				}

				EnterOverrideScope();
		
				if (isAdding)
					animController?.AddAnimOverrides(anim, priority);
				else
					animController?.RemoveAnimOverrides(anim);
			}
			catch (Exception e)
			{
				DebugConsole.LogError(
					$"[AnimSyncer][RECEIVE_OVERRIDE]{EntityName}:{NetId} Failed to process kanim override. {e}");
			}
			finally
			{
				ExitOverrideScope();
			}
		}

		[ClientRpc(SendMode = (int)PacketSendMode.Reliable)]
		private void RpcSetSymbolVisiblity(KAnimHashedString symbol, bool is_visible)
		{
			using var _ = Profiler.Scope();

			if (ENABLE_LOG)
				DebugConsole.LogNonImportant(
					$"[AnimSyncer][SYMBOL][CLIENT]{EntityName}:{NetId} {symbol} visible={is_visible}");

			try
			{
				applyingSymbolVisibility = true;

				// Caution: the typo may be fixed in future game updates, but hope they would not.
				animController?.SetSymbolVisiblity(symbol, is_visible);
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[AnimSyncer][SYMBOL][CLIENT] {EntityName}:{NetId} {ex}");
			}
			finally
			{
				applyingSymbolVisibility = false;
			}
		}

		// This is the lock to allow animations to be played in a synchronized manner on the client.
		private int allowPlaybackDepth = 0;
		public void EnterSyncedPlaybackScope() => allowPlaybackDepth++;
		public void ExitSyncedPlaybackScope() => allowPlaybackDepth = Mathf.Max(allowPlaybackDepth - 1, 0);
		public bool IsInSyncedPlaybackScope() => allowPlaybackDepth > 0;

		// This is the lock to allow animations to be overridden in a synchronized manner on the client.
		private int allowOverrideDepth = 0;
		public void EnterOverrideScope() => allowOverrideDepth++;
		public void ExitOverrideScope() => allowOverrideDepth = Mathf.Max(allowOverrideDepth - 1, 0);
		public bool IsInOverrideScope() => allowOverrideDepth > 0;
	
		private bool applyingSymbolVisibility;
		public bool IsApplyingSymbolVisibility() => applyingSymbolVisibility;
	}
}
