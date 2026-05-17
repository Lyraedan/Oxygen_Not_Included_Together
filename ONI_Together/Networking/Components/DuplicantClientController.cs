/*

using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.DuplicantActions;
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
		private float moveSpeed;
		private bool isLooping;
		private byte endNavType;
		private Vector3 moveDirection;
		private float moveTotalDist;
		private int animCompleteHandle = -1;
		private bool animFinished;
		private Vector3 controlledPosition;

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();
			base.OnSpawn();

			if (!MultiplayerSession.InSession || MultiplayerSession.IsHost)
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

			if (!MultiplayerSession.InSession || MultiplayerSession.IsHost)
				return;

			if (isTransitioning)
				UpdateMovement();
			else
				TryDequeueAndPlay();
		}

		private void UpdateMovement()
		{
			using var _ = Profiler.Scope();

			if (isLooping)
			{
				Vector3 pos = controlledPosition + moveDirection * moveSpeed * Time.deltaTime;
				float traveled = Vector3.Distance(moveStart, pos);

				if (traveled >= moveTotalDist)
				{
					FinishTransition();
				}
				else
				{
					controlledPosition = pos;
					transform.SetPosition(controlledPosition);
				}
			}
			else
			{
				if (animFinished)
					FinishTransition();
			}
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

			if (navigator == null || animController == null)
				return;

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
				controlledPosition = packet.SourcePosition + new Vector3(packet.TransitionX, packet.TransitionY, 0f);
				transform.SetPosition(controlledPosition);
				navigator.SetCurrentNavType((NavType)packet.EndNavType);
				isTransitioning = false;
				return;
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

			if (buffer.Count > 3)
			{
				while (buffer.Count > 1)
				{
					var skip = buffer.Dequeue();
					controlledPosition = skip.SourcePosition + new Vector3(skip.TransitionX, skip.TransitionY, 0f);
				}
				transform.SetPosition(controlledPosition);
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
			moveTotalDist = delta.magnitude;
			moveDirection = delta / moveTotalDist;
			moveSpeed = packet.Speed > 0f ? packet.Speed : FallbackMoveSpeed;
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
					animController.PlaySpeedMultiplier = packet.AnimSpeed;
					animController.Play(anim, KAnim.PlayMode.Loop);
				}
			}
			else
			{
				HashedString preAnim = packet.PreAnim;
				HashedString anim = packet.Anim;

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
				animCompleteHandle = animController.gameObject.Subscribe((int)GameHashes.AnimQueueComplete, OnAnimComplete);
			}

			isTransitioning = true;
		}

		private void OnAnimComplete(object data)
		{
			using var _ = Profiler.Scope();
			animFinished = true;
		}

		public void OnStopReceived(NavType navType)
		{
			using var _ = Profiler.Scope();

			if (navigator == null)
				return;

			pendingStop = true;
			stopNavType = navType;
			buffer.Clear();

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
			animController.PlaySpeedMultiplier = 1f;
			animController.Play(idleAnim, KAnim.PlayMode.Loop);
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

		public void OnStateReceived(DuplicantActionState state, int targetCell, string animName, float animElapsedTime, bool isWorking)
		{
			using var _ = Profiler.Scope();

			if (isTransitioning)
				return;

			if (isWorking && !string.IsNullOrEmpty(animName) && animController != null)
			{
				animController.Play(new HashedString(animName), KAnim.PlayMode.Loop);
			}
		}
	}
}

*/
