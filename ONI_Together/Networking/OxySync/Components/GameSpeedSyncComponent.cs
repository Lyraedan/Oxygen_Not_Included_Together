using KSerialization;
using ONI_Together.Patches;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    public class GameSpeedSyncComponent : NetworkBehaviour
    {
        public enum SpeedState
        {
            Paused = -1,
            Normal = 0,
            Double = 1,
            Triple = 2
        }

        public static GameSpeedSyncComponent? Instance { get; private set; }

        private SpeedState _currentState;
        private float _lastForceSyncTime;
        private const float FORCE_SYNC_INTERVAL = 2f;

        public override void OnSpawn()
        {
            base.OnSpawn();
            Instance = this;
            NetId = nameof(SpeedControlScreen).GetHashCode();
            InterestGroup = -1;

            if (SpeedControlScreen.Instance != null)
            {
                _currentState = SpeedControlScreen.Instance.IsPaused
                    ? SpeedState.Paused
                    : (SpeedState)SpeedControlScreen.Instance.GetSpeed();
            }
        }

        public override void OnCleanUp()
        {
            if (Instance == this)
                Instance = null;
            base.OnCleanUp();
        }

        public void RequestSetSpeed(int speed)
        {
            CallCommand(nameof(CmdSetSpeed), speed);
        }

        [Command]
        private void CmdSetSpeed(int speed)
        {
            ApplyAndBroadcast((SpeedState)speed);
        }

        private void ApplyAndBroadcast(SpeedState state)
        {
            SpeedControlScreen_SendSpeedPacketPatch.IsSyncing = true;
            try
            {
                if (SpeedControlScreen.Instance == null) return;

                if (state == SpeedState.Paused)
                {
                    if (!SpeedControlScreen.Instance.IsPaused)
                        SpeedControlScreen.Instance.TogglePause();
                }
                else
                {
                    if (SpeedControlScreen.Instance.IsPaused)
                        SpeedControlScreen.Instance.TogglePause();
                    SpeedControlScreen.Instance.SetSpeed((int)state);
                }

                _currentState = state;
                CallClientRpc(nameof(RpcApplySpeed), (int)state);
            }
            finally
            {
                SpeedControlScreen_SendSpeedPacketPatch.IsSyncing = false;
            }
        }

        [ClientRpc]
        private void RpcApplySpeed(int state)
        {
            SpeedControlScreen_SendSpeedPacketPatch.IsSyncing = true;
            try
            {
                var speedState = (SpeedState)state;
                if (SpeedControlScreen.Instance == null) return;

                if (speedState == SpeedState.Paused)
                {
                    if (!SpeedControlScreen.Instance.IsPaused)
                        SpeedControlScreen.Instance.TogglePause();
                }
                else
                {
                    if (SpeedControlScreen.Instance.IsPaused)
                        SpeedControlScreen.Instance.TogglePause();
                    SpeedControlScreen.Instance.SetSpeed((int)speedState);
                }

                _currentState = speedState;
            }
            finally
            {
                SpeedControlScreen_SendSpeedPacketPatch.IsSyncing = false;
            }
        }

        private void Update()
        {
            if (!isServer) return;

            if (Time.unscaledTime - _lastForceSyncTime >= FORCE_SYNC_INTERVAL)
            {
                _lastForceSyncTime = Time.unscaledTime;
                CallClientRpc(nameof(RpcApplySpeed), (int)_currentState);
            }
        }
    }
}
