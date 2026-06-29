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

        public override void OnSpawn()
        {
            base.OnSpawn();
            syncRotation = false;
            syncScale = false;
            useSnapshotInterpolation = true;
        }

        [Server]
        protected override void ServerUpdate()
        {
            /* Replaced by Interest Groups
            int cell = Grid.PosToCell(transform.position);
            if (WorldStateSyncer.Instance != null
                && !WorldStateSyncer.Instance.IsCellVisibleToAnyClientViewport(cell, VIEWPORT_MARGIN))
                return;
            */
            
            base.ServerUpdate();

            if (kbac != null)
            {
                _netFlipX = kbac.FlipX;
                _netFlipY = kbac.FlipY;
            }

            if (navigator != null && navigator.CurrentNavType != NavType.NumNavTypes)
                _netNavType = navigator.CurrentNavType;
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
            return false; // Replaced by interest groups
            
            if (!WorldStateSyncer.TryGetLocalViewport(out var viewport))
                return false;

            int cell = Grid.PosToCell(transform.position);
            return WorldStateSyncer.IsCellInRect(cell, viewport);
        }

        protected override void OnServerPositionRequest(ulong requesterId)
        {
            bool flipX = kbac != null && kbac.FlipX;
            bool flipY = kbac != null && kbac.FlipY;
            NavType navType = navigator != null && navigator.CurrentNavType != NavType.NumNavTypes
                ? navigator.CurrentNavType : NavType.Floor;

            CallTargetRpc(requesterId, nameof(TargetRpcReceiveFullState),
                transform.position, flipX, flipY, navType);
        }

        [TargetRpc]
        private void TargetRpcReceiveFullState(Vector3 position, bool flipX, bool flipY, NavType navType)
        {
            _netPosition = position;
            _netFlipX = flipX;
            _netFlipY = flipY;
            _netNavType = navType;

            transform.SetPosition(position);

            if (kbac != null)
            {
                kbac.FlipX = flipX;
                kbac.FlipY = flipY;
            }

            if (navigator != null)
                navigator.SetCurrentNavType(navType);

            _lastRequestTime = Time.unscaledTime;
        }

        private void OnNavTypeChanged(NavType old, NavType current)
        {
            if (navigator != null)
                navigator.SetCurrentNavType(current);
        }
    }
}
