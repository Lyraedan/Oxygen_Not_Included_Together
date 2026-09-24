using System;
using ONI_Together.DebugTools;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    [FixedInterestGroup]
    public class GameSpeedSyncer : NetworkBehaviour
    {
        private static readonly bool ENABLE_LOG = false;

        public enum SpeedMode: int
        {
            Normal = 0,
            Double = 1,
            Triple = 2
        }

        [Serializable]
        public class GameSpeedState
        {
            public SpeedMode Speed;
            public bool Paused;

            public GameSpeedState() { }
            public GameSpeedState(SpeedMode speed, bool paused)
            {
                Speed = speed;
                Paused = paused;
            }
        }

        public static GameSpeedSyncer Instance { get; private set; }

        // Use a counter instead of a boolean so nested/re-entrant vanilla calls
        // cannot clear the applying-state flag before the outer operation completes.
        // More importantly, the vanilla game's logic would call our patched methods many times,
        // so using a counter avoids losing the IsApplyingNetworkState state.
        // Similar to the vanilla game's pause logic, but adapted for network synchronization.
        public bool IsApplyingNetworkState => _applyDepth > 0;
        private int _applyDepth = 0;

        // Current synchronized game speed.
        //
        // We intentionally do not use [SyncVar] here.
        // SyncVar changes are detected by the server controller on a heartbeat,
        // which introduces unnecessary delay for time-sensitive operations such
        // as game-speed changes.
        //
        // Game-speed changes are therefore propagated immediately through
        // Command -> ClientRpc instead of waiting for SyncVar change detection.
        //
        // This also ensures that each accepted speed change is sent at the time
        // it is processed by the server, rather than relying on periodically
        // observed state.
        private uint _revision = 0;
        private uint _lastAppliedRevision = 0;
        private GameSpeedState _authoritativeState;

        public override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Instance = this;
            _applyDepth = 0;
            _revision = 0;
            _lastAppliedRevision = 0;

            NetId = nameof(SpeedControlScreen).GetHashCode();
            InterestGroup = -1;

            if (ENABLE_LOG)
                DebugConsole.Log("[GameSpeedSyncer][OnPrefabInit] Prefab initialized.");
        }

        public override void OnSpawn()
        {
            base.OnSpawn();

            _authoritativeState = new GameSpeedState
            (
                (SpeedMode)(SpeedControlScreen.Instance?.GetSpeed() ?? 0),
                SpeedControlScreen.Instance?.IsPaused ?? true
            );

            if (MultiplayerSession.IsClient)
                RequestReSync();

            if (ENABLE_LOG)
                DebugConsole.Log($"[GameSpeedSyncer][OnSpawn] set speed to {_authoritativeState}.");
        }

        public override void OnCleanUp()
        {
            if (ENABLE_LOG)
                DebugConsole.Log("[GameSpeedSyncer][OnCleanUp] Cleaning up instance.");

            if (Instance == this)
                Instance = null;
            base.OnCleanUp();
        }

        public void RequestReSync()
        {
            try
            {
                CallCommand(nameof(CmdSetSpeed), (object)null);
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[GameSpeedSyncer][RequestReSync] {ex}");
            }
        }

        public void RequestSetSpeed(SpeedMode speed, bool paused)
        {
            try
            {
                if (ENABLE_LOG)
                    DebugConsole.Log(
                        $"[GameSpeedSyncer][RequestSetSpeed] {_authoritativeState} -> {speed}, isApplying: {IsApplyingNetworkState}");

                // The game itself would call to set to same speed, we can ignore those.
                if (IsStateSynchronized(speed, paused))
                    return;

                CallCommand(nameof(CmdSetSpeed), new GameSpeedState(speed, paused));

                if (ENABLE_LOG)
                    DebugConsole.Log(
                        $"[GameSpeedSyncer][RequestSetSpeed] request sent. " +
                        $"Current Speed: {_authoritativeState.Speed}, isPaused: {_authoritativeState.Paused}.");
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[GameSpeedSyncer][RequestSetSpeed] {ex}");
            }
        }

        [Command(SendMode = (int)PacketSendMode.ReliableImmediate)]
        private void CmdSetSpeed(GameSpeedState state)
        {
            if (!isServer)
                return;
            
            if (state!= null && !IsValidSpeed(state.Speed))
                return;

            if (ENABLE_LOG)
                DebugConsole.Log(
                    $"[GameSpeedSyncer][CmdSetSpeed] " +
                    $"Current Speed: {_authoritativeState.Speed} -> {state.Speed}, " +
                    $"IsPaused: {_authoritativeState.Paused} -> {state.Paused}, " +
                    $"isApplying: {IsApplyingNetworkState}, currentRevision: {_revision}");

            if (state != null && !ApplySpeed(state.Speed, state.Paused))
                return;

            _revision++;

            var validatedState = state ?? _authoritativeState;

            try
            {                
                if (MultiplayerSession.SessionHasPlayers)
                    CallClientRpc(nameof(RpcApplySpeed), _revision, validatedState.Speed, validatedState.Paused);

                if (ENABLE_LOG)
                    DebugConsole.Log(
                        $"[GameSpeedSyncer][CmdSetSpeed] complete setting speed. " +
                        $"CurrentSpeed: {_authoritativeState.Speed}, IsPaused: {_authoritativeState.Paused}, " +
                        $"currentRevision: {_revision}.");
            }
            catch (Exception ex)
            {
                DebugConsole.LogError(
                    $"[GameSpeedSyncer][CmdSetSpeed] Failed to set speed to {validatedState.Speed}, " +
                    $"isApplying: {IsApplyingNetworkState}, currentRevision: {_revision}. {ex}");
            }
        }

        [ClientRpc(SendMode = (int)PacketSendMode.ReliableImmediate)]
        private void RpcApplySpeed(uint revision, SpeedMode state, bool isPaused)
        {
            if (!IsValidSpeed(state))
                return;

            if (ENABLE_LOG)
                DebugConsole.Log(
                    $"[GameSpeedSyncer][RpcApplySpeed] " +
                    $"CurrentSpeed: {_authoritativeState.Speed} -> {state}, " +
                    $"IsPaused: {_authoritativeState.Paused} -> {isPaused}, " +
                    $"revision: {_lastAppliedRevision} -> {revision}, " +
                    $"isApplying: {IsApplyingNetworkState}.");

            if (revision <= _lastAppliedRevision)
                return;

            if (!ApplySpeed(state, isPaused))
                return;

            _lastAppliedRevision = revision;
            
            if (ENABLE_LOG)
                DebugConsole.Log($"[GameSpeedSyncer][RpcApplySpeed] RPC complete applying speed. Current: {_authoritativeState}.");
        }

        private bool ApplySpeed(SpeedMode state, bool isPaused)
        {
            _applyDepth++;
            try
            {
                SpeedControlScreen screen = SpeedControlScreen.Instance;
                if (screen == null)
                    return false;

                if (_authoritativeState == null)
                    return true;

                if (ENABLE_LOG)
                    DebugConsole.LogNonImportant(
                        $"[GameSpeedSyncer][ApplySpeed] " +
                        $"CurrentSpeed: {_authoritativeState.Speed}, " +
                        $"IsPaused: {_authoritativeState.Paused}, " +
                        $"isApplying: {IsApplyingNetworkState}, " +
                        $"isGamePaused: {screen.IsPaused}, " +
                        $"gameSpeed: {screen.GetSpeed()}");

                screen.SetSpeed((int)state);
                _authoritativeState.Speed = state;

                if (isPaused && !screen.IsPaused)
                {
                    screen.TogglePause();
                }
                else if (!isPaused)
                {
                    while (screen.IsPaused)
                        screen.TogglePause();
                }

                if (ENABLE_LOG)
                    DebugConsole.LogNonImportant($"[GameSpeedSyncer][ApplySpeed] Applied speed {state}, origin: {_authoritativeState}.");

                _authoritativeState.Paused = isPaused;
                return true;
            }
            catch (Exception ex)
            {
                DebugConsole.LogError(
                    $"[GameSpeedSyncer][ApplySpeed] " +
                    $"Current: {_authoritativeState}, Expected: {state}. {ex}");

                return false;
            }
            finally
            {
                _applyDepth = Mathf.Max(0, _applyDepth - 1);
            }
        }

        public bool IsStateSynchronized(SpeedMode state, bool isPaused)
        {
            if (_authoritativeState == null || SpeedControlScreen.Instance == null)
                return false;
            bool isSpeedSynced = _authoritativeState.Speed == state && SpeedControlScreen.Instance.GetSpeed() == (int)state;
            bool isPauseSynced = _authoritativeState.Paused == isPaused && SpeedControlScreen.Instance.IsPaused == isPaused;
            return isSpeedSynced && isPauseSynced;
        }

        public static bool IsValidSpeed(SpeedMode state)
        {
            return state == SpeedMode.Normal
                || state == SpeedMode.Double
                || state == SpeedMode.Triple;
        }
    }
}
