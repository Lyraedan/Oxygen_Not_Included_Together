/*

using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.DuplicantActions;
using ONI_Together.Patches.KleiPatches;
using Shared.Profiling;
using System.Collections.Generic;
using UnityEngine;

namespace ONI_Together.Networking.Components
{
	public class DuplicantClientController : KMonoBehaviour
	{
		[MyCmpGet] private Navigator navigator;
		[MyCmpGet] private KBatchedAnimController animController;
		[MyCmpGet] private Facing facing;

		public bool IsMoving { get; private set; }
		public bool OwnsPosition => isTransitioning || buffer.Count > 0;

		private readonly Queue<NavigatorTransitionPacket> buffer = new Queue<NavigatorTransitionPacket>(16);
		private const int MaxBufferSize = 16;
		private const float BufferTargetDelay = 0.08f;
		private bool receivedFirstPacket;
		private float firstPacketTime;
		private bool playbackStarted;

		private const float CorrectionSnapDistance = 3;
		private const float FallbackMoveSpeed = 3f;
		private NavType stopNavType;
		private bool pendingStop;

		private bool isTransitioning;
		private Vector3 moveStart;
		private Vector3 moveTarget;
		private bool isLooping;
		private byte endNavType;
		private int animCompleteHandle = -1;
		private bool animFinished;
		private Vector3 controlledPosition;
		private uint lastSequence;
		private float transitionStartedAt;
		private float transitionDuration;
		private const float MinimumTransitionDuration = 0.05f;
		private const float MaximumTransitionDuration = 2.5f;

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();
			base.OnSpawn();

			if (!MultiplayerSession.InActiveSession || MultiplayerSession.IsHost)
			{
				enabled = false;
				return;
			}

			if (navigator == null || animController == null)
			{
				enabled = false;
				return;
			}

			controlledPosition = transform.GetPosition();

			navigator.transitionDriver?.EndTransition();
		}

		private void Update()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession || MultiplayerSession.IsHost)
				return;

			if (isTransitioning)
				UpdateMovement();
			else
				TryDequeueAndPlay();
		}

		private void UpdateMovement()
		{
			using var _ = Profiler.Scope();

			float elapsed = Time.unscaledTime - transitionStartedAt;
			float progress = Mathf.Clamp01(elapsed / transitionDuration);
			controlledPosition = Vector3.Lerp(moveStart, moveTarget, progress);
			transform.SetPosition(controlledPosition);

			if (progress >= 1f || (!isLooping && animFinished && progress >= 0.8f))
				FinishTransition();
		}

		private void FinishTransition()
		{
			using var _ = Profiler.Scope();

			controlledPosition = moveTarget;
			transform.SetPosition(controlledPosition);
			navigator.SetCurrentNavType((NavType)endNavType);
			isTransitioning = false;

			if (animCompleteHandle != -1)
			{
				animController.gameObject.Unsubscribe(animCompleteHandle);
				animCompleteHandle = -1;
			}

			if (pendingStop)
				ApplyStop();
			else
				TryDequeueAndPlay();
		}

		public void OnTransitionReceived(NavigatorTransitionPacket packet)
		{
			using var _ = Profiler.Scope();

			if (navigator == null || animController == null || !IsNewerSequence(packet.Sequence, lastSequence))
				return;

			lastSequence = packet.Sequence;

			pendingStop = false;
			IsMoving = true;

			if (!receivedFirstPacket)
			{
				receivedFirstPacket = true;
				firstPacketTime = Time.unscaledTime;
			}

			if (buffer.Count >= MaxBufferSize)
			{
				buffer.Clear();
				playbackStarted = true;
				controlledPosition = packet.SourcePosition;
				transform.SetPosition(controlledPosition);
				navigator.SetCurrentNavType((NavType)packet.StartNavType);
				isTransitioning = false;
			}

			buffer.Enqueue(packet);
		}

		private void TryDequeueAndPlay()
		{
			using var _ = Profiler.Scope();

			if (buffer.Count == 0)
			{
				if (pendingStop)
					ApplyStop();
				return;
			}

			if (!playbackStarted)
			{
				float timeSinceFirst = Time.unscaledTime - firstPacketTime;
				if (timeSinceFirst < BufferTargetDelay && buffer.Count < 3)
					return;

				playbackStarted = true;
			}

			PlayTransition(buffer.Dequeue());
		}

		private void PlayTransition(NavigatorTransitionPacket packet)
		{
			using var _ = Profiler.Scope();

			if (animCompleteHandle != -1)
			{
				animController.gameObject.Unsubscribe(animCompleteHandle);
				animCompleteHandle = -1;
			}

			controlledPosition = packet.SourcePosition;
			transform.SetPosition(controlledPosition);

			moveStart = controlledPosition;
			var delta = new Vector3(packet.TransitionX, packet.TransitionY, 0f);
			moveTarget = moveStart + delta;
			float moveTotalDist = delta.magnitude;
			float moveSpeed = packet.Speed > 0f ? packet.Speed : FallbackMoveSpeed;
			float catchUpMultiplier = 1f + Mathf.Min(buffer.Count, 4) * 0.2f;
			transitionDuration = Mathf.Clamp(moveTotalDist / Mathf.Max(moveSpeed * catchUpMultiplier, 0.01f),
				MinimumTransitionDuration, MaximumTransitionDuration);
			transitionStartedAt = Time.unscaledTime;
			isLooping = packet.IsLooping;
			endNavType = packet.EndNavType;
			animFinished = false;

			navigator.SetCurrentNavType((NavType)packet.StartNavType);

			if (packet.TransitionX != 0 && facing != null)
				facing.SetFacing(packet.TransitionX < 0);

			if (isLooping)
			{
				HashedString anim = packet.Anim;
				if (anim.IsValid)
				{
					PlayAuthoritativeAnimation(() =>
					{
						animController.PlaySpeedMultiplier = packet.AnimSpeed;
						animController.Play(anim, KAnim.PlayMode.Loop);
					});
				}
			}
			else
			{
				HashedString preAnim = packet.PreAnim;
				HashedString anim = packet.Anim;

				PlayAuthoritativeAnimation(() =>
				{
					if (preAnim.IsValid)
					{
						animController.Play(preAnim, KAnim.PlayMode.Once);
						if (anim.IsValid)
							animController.Queue(anim, KAnim.PlayMode.Once);
					}
					else if (anim.IsValid)
					{
						animController.Play(anim, KAnim.PlayMode.Once);
					}

					animController.PlaySpeedMultiplier = packet.AnimSpeed;
				});
				animCompleteHandle = animController.gameObject.Subscribe((int)GameHashes.AnimQueueComplete, OnAnimComplete);
			}

			isTransitioning = true;
		}

		private void OnAnimComplete(object data)
		{
			using var _ = Profiler.Scope();
			animFinished = true;
		}

		public void OnStopReceived(NavType navType, Vector3 serverPosition, uint sequence)
		{
			using var _ = Profiler.Scope();

			if (navigator == null || !IsNewerSequence(sequence, lastSequence))
				return;

			lastSequence = sequence;

			pendingStop = true;
			stopNavType = navType;
			buffer.Clear();
			controlledPosition = serverPosition;
			transform.SetPosition(serverPosition);

			if (!isTransitioning)
				ApplyStop();
		}

		private void ApplyStop()
		{
			using var _ = Profiler.Scope();

			pendingStop = false;
			IsMoving = false;
			isTransitioning = false;

			if (animCompleteHandle != -1)
			{
				animController.gameObject.Unsubscribe(animCompleteHandle);
				animCompleteHandle = -1;
			}

			navigator.SetCurrentNavType(stopNavType);

			HashedString idleAnim = navigator.NavGrid.GetIdleAnim(stopNavType);
			PlayAuthoritativeAnimation(() =>
			{
				animController.PlaySpeedMultiplier = 1f;
				animController.Play(idleAnim, KAnim.PlayMode.Loop);
			});
		}

		public void OnPositionCorrection(Vector3 serverPosition)
		{
			using var _ = Profiler.Scope();

			if (isTransitioning || IsMoving)
				return;

			float error = Vector3.Distance(controlledPosition, serverPosition);
			if (error > CorrectionSnapDistance)
			{
				controlledPosition = serverPosition;
				transform.SetPosition(controlledPosition);
			}
		}

		internal static bool IsNewerSequence(uint incoming, uint current)
		{
			return incoming != current && unchecked(incoming - current) < 0x80000000u;
		}

		public void OnStateReceived(DuplicantActionState state, int targetCell, string animName, float animElapsedTime, bool isWorking)
		{
			using var _ = Profiler.Scope();

			if (isTransitioning)
				return;

			if (isWorking && !string.IsNullOrEmpty(animName) && animController != null)
			{
				PlayAuthoritativeAnimation(() =>
					animController.Play(new HashedString(animName), KAnim.PlayMode.Loop));
			}
		}

		private static void PlayAuthoritativeAnimation(System.Action action)
		{
			KAnimControllerBase_Patches.AllowAnims();
			try
			{
				action();
			}
			finally
			{
				KAnimControllerBase_Patches.ForbidAnims();
			}
		}
	}
}

*/
