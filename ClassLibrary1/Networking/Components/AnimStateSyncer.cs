using ONI_MP.Networking.Packets.Animation;
using Shared.Profiling;
using UnityEngine;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Core;

namespace ONI_MP.Networking.Components
{
	public class AnimStateSyncer : KMonoBehaviour
	{
		private const float MissingSnapshotRetryDelay = 3f;

		[MyCmpGet]
		private NetworkIdentity networkIdentity;
		[MyCmpGet]
		private KBatchedAnimController animController;
		[MyCmpGet]
		private KPrefabID prefabId;
		[MyCmpGet]
		private Operational operational;

		private float _spawnTime;
		private bool _hasReceivedSnapshot;

		public int NetId => networkIdentity != null ? networkIdentity.NetId : 0;

		public bool HasReceivedSnapshot => MultiplayerSession.IsHost || _hasReceivedSnapshot;

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();

			base.OnSpawn();

			_spawnTime = Time.unscaledTime;

			if (networkIdentity == null || animController == null || prefabId == null)
			{
				enabled = false;
				return;
			}

			if (!AnimSyncEligibility.IsAnimatedNonMinion(gameObject))
			{
				enabled = false;
				return;
			}

			AnimSyncCoordinator.Register(this);
		}

		public override void OnCleanUp()
		{
			using var _ = Profiler.Scope();

			AnimSyncCoordinator.Unregister(this);
			if (networkIdentity != null && networkIdentity.NetId != 0)
				PlayAnimPacket.ForgetNetId(networkIdentity.NetId);
			base.OnCleanUp();
		}

		internal bool TryBuildSnapshot(out AnimSyncPacket packet, out int activityKey)
		{
			using var _ = Profiler.Scope();

			packet = null;
			activityKey = 0;

			try
			{
				if (networkIdentity.NetId == 0)
				{
					// Late-spawned entities may not have a NetId on the first coordinator tick.
					networkIdentity.RegisterIdentity();
					if (networkIdentity.NetId == 0)
						return false;
				}

				if (animController.CurrentAnim == null)
					return false;

				int animHash = animController.currentAnim.hash;
				if (animHash == 0)
					return false;

				byte mode = (byte)animController.mode;
				float speed = animController.playSpeed;
				float elapsedTime = animController.GetElapsedTime();

				packet = new AnimSyncPacket
				{
					NetId = networkIdentity.NetId,
					AnimHash = animHash,
					Mode = mode,
					Speed = speed,
					ElapsedTime = elapsedTime
				};

				activityKey = BuildActivityKey(animHash, mode, speed);
				return true;
			}
			catch (System.Exception)
			{
				return false;
			}
		}

		public void MarkSnapshotReceived()
		{
			using var _ = Profiler.Scope();

			_hasReceivedSnapshot = true;
		}

		public int GetGridCell()
		{
			using var _ = Profiler.Scope();

			return Grid.PosToCell(gameObject);
		}

		public bool IsVisibleIn(RectInt viewport)
		{
			using var _ = Profiler.Scope();

			int cell = GetGridCell();
			if (!Grid.IsValidCell(cell))
				return false;

			Grid.CellToXY(cell, out int x, out int y);
			return x >= viewport.xMin
				&& x < viewport.xMax
				&& y >= viewport.yMin
				&& y < viewport.yMax;
		}

		public bool NeedsInitialSnapshot()
		{
			using var _ = Profiler.Scope();

			return !HasReceivedSnapshot && Time.unscaledTime - _spawnTime >= MissingSnapshotRetryDelay;
		}

		private int BuildActivityKey(int animHash, byte mode, float speed)
		{
			using var _ = Profiler.Scope();

			int operationalMask = 0;
			if (operational != null)
			{
				if (operational.IsOperational)
					operationalMask |= 1;
				if (operational.IsActive)
					operationalMask |= 2;
				if (operational.IsFunctional)
					operationalMask |= 4;
			}

			int speedKey = Mathf.RoundToInt(speed * 100f);

			unchecked
			{
				int key = animHash;
				key = (key * 397) ^ mode;
				key = (key * 397) ^ speedKey;
				key = (key * 397) ^ operationalMask;
				return key;
			}
		}
	}
}
