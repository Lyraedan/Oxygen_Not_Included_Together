using ONI_Together.DebugTools;
using KSerialization;
using ONI_Together.Patches;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    [FixedInterestGroup]
    public class GameSpeedSyncer : NetworkBehaviour
    {
        public enum SpeedState
        {
            Paused = -1,
            Normal = 0,
            Double = 1,
            Triple = 2
        }

        public static GameSpeedSyncer? Instance { get; private set; }

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
            var requested = (SpeedState)speed;

            // Authority choke point for a client-originated resume. This method is the only
            // way a client can change the sim speed (CommandPacket.OnDispatched -> host ->
            // InvokeCommand), and rejecting here covers both halves in one place: the host
            // neither applies the speed locally nor fans it out via RpcApplySpeed, because
            // ApplyAndBroadcast does both. Pausing is always allowed - only resume is gated.
            //
            // The host's own resume attempts are already stopped upstream by the
            // SpeedControlPatch prefixes, so this is a second, independent layer rather than
            // a duplicate: both resolve through the single ReadyManager.CanHostResume()
            // predicate, and a blocked call here simply leaves the sim as it was.
            if (requested != SpeedState.Paused && !ReadyManager.CanHostResume())
            {
                DebugConsole.Log(
                    $"[GameSpeedSyncer] Rejected remote resume to {requested}: not all players are ready");
                ReadyManager.RefreshScreen();

                // The requesting client already applied the resume to its own screen before
                // asking (the postfix runs after the original). Re-assert the authoritative
                // state now instead of letting it run until the next force-sync tick, so the
                // rejection lands within a round trip rather than up to FORCE_SYNC_INTERVAL.
                CallClientRpc(nameof(RpcApplySpeed), (int)_currentState);
                return;
            }

            ApplyAndBroadcast(requested);
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
