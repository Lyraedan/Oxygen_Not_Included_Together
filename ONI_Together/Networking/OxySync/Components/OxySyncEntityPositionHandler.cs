using KSerialization;
using ONI_Together.Networking.Components;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    public class OxySyncEntityPositionHandler : NetworkBehaviour
    {
        [MyCmpGet]
        private KBatchedAnimController kbac;
        [MyCmpGet]
        private Navigator navigator;

        [SyncVar(Epsilon = 0.01f)]
        private Vector3 _netPosition;

        [SyncVar]
        private bool _netFlipX;
        [SyncVar]
        private bool _netFlipY;

        [SyncVar(Hook = nameof(OnNavTypeChanged))]
        private NavType _netNavType;

        private const float SNAP_DISTANCE = 1.5f;
        private const float LERP_SPEED = 20f;
        private const float REQUEST_COOLDOWN = 0.5f;
        private const int VIEWPORT_MARGIN = 2;

        private float _lastRequestTime;

        public override void OnSpawn()
        {
            base.OnSpawn();
            SyncInterval = 0.05f;
        }

        private void Update()
        {
            if (NetId == 0 || !inSession) return;

            if (isServer)
                UpdateServer();
            else
                UpdateClient();
        }

        [Server]
        private void UpdateServer()
        {
            int cell = Grid.PosToCell(transform.position);
            if (WorldStateSyncer.Instance != null
                && !WorldStateSyncer.Instance.IsCellVisibleToAnyClientViewport(cell, VIEWPORT_MARGIN))
                return;

            _netPosition = transform.position;

            if (kbac != null)
            {
                _netFlipX = kbac.FlipX;
                _netFlipY = kbac.FlipY;
            }

            if (navigator != null && navigator.CurrentNavType != NavType.NumNavTypes)
                _netNavType = navigator.CurrentNavType;
        }

        [Client]
        private void UpdateClient()
        {
            if (kbac != null)
            {
                kbac.FlipX = _netFlipX;
                kbac.FlipY = _netFlipY;
            }

            Vector3 currentPos = transform.position;
            float error = Vector3.Distance(currentPos, _netPosition);

            if (error > SNAP_DISTANCE)
            {
                transform.SetPosition(_netPosition);
            }
            else
            {
                float t = Mathf.Clamp01(LERP_SPEED * Time.unscaledDeltaTime);
                transform.SetPosition(Vector3.Lerp(currentPos, _netPosition, t));
            }

            TryRequestPosition();
        }

        private void OnNavTypeChanged(NavType old, NavType current)
        {
            if (navigator != null)
                navigator.SetCurrentNavType(current);
        }

        private void TryRequestPosition()
        {
            if (!WorldStateSyncer.TryGetLocalViewport(out var viewport))
                return;

            int cell = Grid.PosToCell(transform.position);
            if (!WorldStateSyncer.IsCellInRect(cell, viewport))
                return;

            if (Time.unscaledTime - _lastRequestTime < REQUEST_COOLDOWN)
                return;

            _lastRequestTime = Time.unscaledTime;
            CallCommand(nameof(CmdRequestPositionSync), MultiplayerSession.LocalUserID);
        }

        [Command]
        private void CmdRequestPositionSync(ulong requesterId)
        {
            Vector3 pos = transform.position;
            bool flipX = kbac != null && kbac.FlipX;
            bool flipY = kbac != null && kbac.FlipY;
            NavType navType = navigator != null && navigator.CurrentNavType != NavType.NumNavTypes
                ? navigator.CurrentNavType : NavType.Floor;
            
            CallTargetRpc(requesterId, nameof(RpcReceivePosition), pos, flipX, flipY, navType);
        }

        [TargetRpc]
        private void RpcReceivePosition(Vector3 position, bool flipX, bool flipY, NavType navType)
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
        }
    }
}
