using KSerialization;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DuplicantActions;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using System;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    public class OxySyncEntityPositionHandler : NetworkTransform
    {
        [MyCmpGet]
        private KBatchedAnimController kbac;
        [MyCmpGet]
        private Navigator navigator;

        [SyncVar]
        private bool _netFlipX;
        [SyncVar]
        private bool _netFlipY;

        [SyncVar(Hook = nameof(OnNavTypeChanged))]
        private NavType _netNavType;

		private static readonly int NetFlipXHash = OxySyncHash.Compute(nameof(_netFlipX));
		private static readonly int NetFlipYHash = OxySyncHash.Compute(nameof(_netFlipY));
		private static readonly int NetNavTypeHash = OxySyncHash.Compute(nameof(_netNavType));

        private const int VIEWPORT_MARGIN = 2;
        private const float STALE_THRESHOLD = 2f;
        private const float HEARTBEAT_INTERVAL = 1f;
        private float _lastSyncReceivedTime;
        private float _lastHeartbeatTime;
        private Vector3 _lastPosition;

		private DuplicantClientController _duplicantPlayback;
		private bool _isDuplicant;
		private bool _hasVisualSnapshot;
		private Vector3 _lastVisualSnapshotPosition;
		private NavType _lastVisualSnapshotNavType;
		private DuplicantVisualSnapshotFlags _lastVisualSnapshotFlags;
		private float _lastVisualSnapshotTime;
		private uint _visualSnapshotSequence;

		internal bool IsDuplicant => _isDuplicant;

        public override void OnSpawn()
        {
			if (kbac == null) kbac = GetComponent<KBatchedAnimController>();
			if (navigator == null) navigator = GetComponent<Navigator>();
			_isDuplicant = (GetComponent<KPrefabID>()?.HasTag(GameTags.BaseMinion) ?? false) || GetComponent<MinionIdentity>() != null;
            base.OnSpawn();
            syncRotation = false;
            syncScale = false;
            useSnapshotInterpolation = true;
            bufferTimeMultiplier = 3.0;
			if (_isDuplicant)
			{
				// Duplicants use their navigation-aware playback controller. Keep the
				// generic transform buffer as a legacy fallback only.
				bufferTimeMultiplier = 2.0;
				maxAdaptiveBufferMilliseconds = 200.0;
			}
            _lastHeartbeatTime = Time.unscaledTime;
            _lastPosition = transform.position;
			_lastVisualSnapshotPosition = _lastPosition;
        }

        [Server]
        protected override void ServerUpdate()
        {
            base.ServerUpdate();

			if (Time.unscaledTime - _lastHeartbeatTime < HEARTBEAT_INTERVAL)
				return;
			
			bool hasUpdate = false;

            if (kbac != null)
            {
                if (_netFlipX != kbac.FlipX)
                {
					_netFlipX = kbac.FlipX;
					SetSyncVarDirty(NetFlipXHash);
					hasUpdate = true;
				}

				if (_netFlipY != kbac.FlipY)
				{
					_netFlipY = kbac.FlipY;
					SetSyncVarDirty(NetFlipYHash);
					hasUpdate = true;
				}
            }

			if (navigator != null && navigator.CurrentNavType != NavType.NumNavTypes && _netNavType != navigator.CurrentNavType)
			{
					_netNavType = navigator.CurrentNavType;
					SetSyncVarDirty(NetNavTypeHash);
					hasUpdate = true;
			}

            Vector3 currentPos = transform.position;
            if (Vector3.Distance(currentPos, _lastPosition) >= 0.01f)
            {
                _lastHeartbeatTime = Time.unscaledTime;
                _lastPosition = currentPos;
				hasUpdate = true;
            }

			if (hasUpdate)
				_lastHeartbeatTime = Time.unscaledTime;
        }

        public override bool ApplySyncVar(int fieldHash, object value, long timestamp)
        {
            bool applied = base.ApplySyncVar(fieldHash, value, timestamp);
            if (applied)
                _lastSyncReceivedTime = Time.unscaledTime;
            return applied;
        }

        [Client]
        protected override void ClientUpdate()
        {
            base.ClientUpdate();

            // Once the navigation-aware controller is active it is the only client
            // writer for both the transform and facing. Reapplying legacy SyncVars
            // here would race the buffered snapshot selected later in the frame.
            if (kbac != null && !TryGetDuplicantPlayback(out _))
            {
                kbac.FlipX = _netFlipX;
                kbac.FlipY = _netFlipY;
            }
        }

		protected override bool ShouldApplyClientState()
		{
			return !TryGetDuplicantPlayback(out _);
		}

		internal bool TryCaptureVisualSnapshot(bool force, bool explicitTeleport,
			out DuplicantVisualSnapshot snapshot)
		{
			snapshot = default;
			if (!_isDuplicant || !MultiplayerSession.IsHost || NetId == 0)
				return false;

			Vector3 position = transform.position;
			bool flipX = kbac != null && kbac.FlipX;
			bool flipY = kbac != null && kbac.FlipY;
			bool moving = navigator != null && navigator.IsMoving();
			NavType navType = navigator != null && navigator.CurrentNavType != NavType.NumNavTypes
				? navigator.CurrentNavType
				: NavType.Floor;

			var flags = DuplicantVisualSnapshotFlags.None;
			if (flipX) flags |= DuplicantVisualSnapshotFlags.FlipX;
			if (flipY) flags |= DuplicantVisualSnapshotFlags.FlipY;
			if (moving) flags |= DuplicantVisualSnapshotFlags.Moving;

			float distance = _hasVisualSnapshot
				? Vector3.Distance(position, _lastVisualSnapshotPosition)
				: float.MaxValue;
			bool changed = distance >= 1f / DuplicantVisualSnapshot.PositionScale
				|| navType != _lastVisualSnapshotNavType
				|| flags != _lastVisualSnapshotFlags;
			bool heartbeat = Time.unscaledTime - _lastVisualSnapshotTime >= HEARTBEAT_INTERVAL;
			if (!force && !changed && !heartbeat)
				return false;

			if (explicitTeleport || (_hasVisualSnapshot && distance > 3f))
				flags |= DuplicantVisualSnapshotFlags.Teleport;

			_visualSnapshotSequence++;
			if (_visualSnapshotSequence == 0)
				_visualSnapshotSequence++;

			snapshot = new DuplicantVisualSnapshot
			{
				NetId = NetId,
				Sequence = _visualSnapshotSequence,
				Position = position,
				NavType = navType,
				Flags = flags,
			};

			_hasVisualSnapshot = true;
			_lastVisualSnapshotPosition = position;
			_lastVisualSnapshotNavType = navType;
			_lastVisualSnapshotFlags = flags & ~DuplicantVisualSnapshotFlags.Teleport;
			_lastVisualSnapshotTime = Time.unscaledTime;
			return true;
		}

		internal void ReceiveVisualSnapshot(DuplicantVisualSnapshot snapshot, long serverTimestamp)
		{
			if (!_isDuplicant)
				return;

			_netPosition = snapshot.Position;
			_netFlipX = snapshot.FlipX;
			_netFlipY = snapshot.FlipY;
			_netNavType = snapshot.NavType;
			_lastSyncReceivedTime = Time.unscaledTime;

			if (TryGetDuplicantPlayback(out var playback))
			{
				playback.OnSnapshotReceived(snapshot, serverTimestamp);
				return;
			}

			// The multiplayer initializer intentionally waits for the session to be
			// ready. Preserve a correct fallback position during that short window.
			transform.SetPosition(snapshot.Position);
			if (navigator != null)
				navigator.SetCurrentNavType(snapshot.NavType);
		}

		private bool TryGetDuplicantPlayback(out DuplicantClientController playback)
		{
			if (!_isDuplicant || !MultiplayerSession.IsClient)
			{
				playback = null;
				return false;
			}

			if (_duplicantPlayback == null)
				_duplicantPlayback = GetComponent<DuplicantClientController>();

			playback = _duplicantPlayback;
			return playback != null && playback.IsPlaybackActive;
		}

        protected override bool ShouldRequestPosition()
        {
            if (!WorldStateSyncer.TryGetLocalViewport(out var viewport))
                return false;

            int cell = Grid.PosToCell(transform.position);
            if (!WorldStateSyncer.IsCellInRect(cell, viewport))
                return false;

            return Time.unscaledTime - _lastSyncReceivedTime > STALE_THRESHOLD;
        }

        protected override void OnServerPositionRequest(ulong requesterId)
        {
			if (_isDuplicant && TryCaptureVisualSnapshot(true, false, out var snapshot))
			{
				CallTargetRpc(requesterId, nameof(TargetRpcReceiveFullState),
					snapshot.Position, snapshot.FlipX, snapshot.FlipY, snapshot.NavType,
					snapshot.Sequence, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
				return;
			}

			bool flipX = kbac != null && kbac.FlipX;
			bool flipY = kbac != null && kbac.FlipY;
			NavType navType = navigator != null && navigator.CurrentNavType != NavType.NumNavTypes
				? navigator.CurrentNavType : NavType.Floor;

			CallTargetRpc(requesterId, nameof(TargetRpcReceiveFullState),
				transform.position, flipX, flipY, navType, 0u,
				DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        [TargetRpc]
		private void TargetRpcReceiveFullState(Vector3 position, bool flipX, bool flipY, NavType navType,
			uint sequence, long serverTimestamp)
        {
            _netPosition = position;
            _netFlipX = flipX;
            _netFlipY = flipY;
            _netNavType = navType;

			var flags = DuplicantVisualSnapshotFlags.Teleport;
			if (flipX) flags |= DuplicantVisualSnapshotFlags.FlipX;
			if (flipY) flags |= DuplicantVisualSnapshotFlags.FlipY;
			var snapshot = new DuplicantVisualSnapshot
			{
				NetId = NetId,
				Sequence = sequence,
				Position = position,
				NavType = navType,
				Flags = flags,
			};

			if (TryGetDuplicantPlayback(out var playback))
				playback.OnSnapshotReceived(snapshot, serverTimestamp);
			else
				transform.SetPosition(position);

            if (kbac != null)
            {
                kbac.FlipX = flipX;
                kbac.FlipY = flipY;
            }

            if (navigator != null)
                navigator.SetCurrentNavType(navType);

            _lastSyncReceivedTime = Time.unscaledTime;
            _lastRequestTime = Time.unscaledTime;
        }

        private void OnNavTypeChanged(NavType old, NavType current)
        {
			if (!TryGetDuplicantPlayback(out _) && navigator != null)
                navigator.SetCurrentNavType(current);
        }
    }
}
