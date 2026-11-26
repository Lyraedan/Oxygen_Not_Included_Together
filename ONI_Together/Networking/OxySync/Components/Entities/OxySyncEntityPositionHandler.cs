using KSerialization;
using ONI_Together.Networking.Components;
using Shared.OxySync;
using Shared.OxySync.Attributes;
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

        private const int VIEWPORT_MARGIN = 2;
        private const float STALE_THRESHOLD = 2f;
        private const float HEARTBEAT_INTERVAL = 1f;
        private float _lastSyncReceivedTime;
        private float _lastHeartbeatTime;
        private Vector3 _lastPosition;

        private bool _isMinion;

        public override void OnSpawn()
        {
            _isMinion = TryGetComponent<MinionIdentity>(out _);
            if (_isMinion)
                InterestGroup = -1; // Always broadcast duplicant transforms so chunk-edge dropouts never trigger 0.5s resync teleports
            base.OnSpawn();
            syncRotation = false;
            syncScale = false;
            useSnapshotInterpolation = true;
            updateMethod = UpdateMethod.LateUpdate;
            if (_isMinion)
            {
                SyncInterval = 0.033f; // 30 Hz for duplicants
                InterestGroup = -1;
            }
            _lastHeartbeatTime = Time.unscaledTime;
            _lastSyncReceivedTime = Time.unscaledTime;
            _lastPosition = transform.position;
        }

        [Server]
        protected override void ServerUpdate()
        {
            base.ServerUpdate();

            if (kbac != null)
            {
                _netFlipX = kbac.FlipX;
                _netFlipY = kbac.FlipY;
            }

            if (navigator != null && navigator.CurrentNavType != NavType.NumNavTypes)
                _netNavType = navigator.CurrentNavType;

            Vector3 currentPos = transform.position;
            if (Vector3.Distance(currentPos, _lastPosition) >= 0.01f)
            {
                _lastHeartbeatTime = Time.unscaledTime;
                _lastPosition = currentPos;
            }
            else if (Time.unscaledTime - _lastHeartbeatTime >= HEARTBEAT_INTERVAL)
            {
                //MarkAllDirty();
                MarkSyncVarAsDirty(NetPositionHash);
                _lastHeartbeatTime = Time.unscaledTime;
            }
        }

        public override void ApplySyncVar(int fieldHash, object value, long timestamp)
        {
            base.ApplySyncVar(fieldHash, value, timestamp);
            _lastSyncReceivedTime = Time.unscaledTime;
        }

        [Client]
        protected override void ClientUpdate()
        {
            base.ClientUpdate();

            if (kbac != null)
            {
                kbac.FlipX = _netFlipX;
                kbac.FlipY = _netFlipY;
            }
        }

        protected override bool ShouldRequestPosition()
        {
            if (!WorldStateSyncer.TryGetLocalViewport(out var viewport))
                return false;

            int cell = Grid.PosToCell(transform.position);
            int margin = WorldChunkHelper.ChunkSize * 2;
            if (!WorldStateSyncer.IsCellInRect(cell, viewport, margin)) return false;
            return Time.unscaledTime - _lastSyncReceivedTime > STALE_THRESHOLD;
        }

        protected override void OnServerPositionRequest(ulong requesterId)
        {
            bool flipX = kbac != null && kbac.FlipX;
            bool flipY = kbac != null && kbac.FlipY;
            NavType navType = navigator != null && navigator.CurrentNavType != NavType.NumNavTypes ? navigator.CurrentNavType : NavType.Floor;
            CallTargetRpc(requesterId, nameof(TargetRpcReceiveFullState), transform.position, flipX, flipY, navType);
        }

        [TargetRpc]
        private void TargetRpcReceiveFullState(Vector3 position, bool flipX, bool flipY, NavType navType)
        {
            // Keep cached sync fields and snapshot buffer in sync so ClientUpdate
            // does not snap the entity right back to the stale pre-resync position.
            _netPosition = position;
            _netFlipX = flipX;
            _netFlipY = flipY;
            _netNavType = navType;
            ResetSnapshots(position, _netRotation, _netScale);

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
            if (navigator != null)
                navigator.SetCurrentNavType(current);
        }
    }
}