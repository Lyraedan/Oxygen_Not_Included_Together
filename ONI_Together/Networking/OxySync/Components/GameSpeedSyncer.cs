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

        public enum SpeedState: int
        {
            Paused = -1,
            Normal = 0,
            Double = 1,
            Triple = 2
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
        private SpeedState _authoritativeState;

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

            _authoritativeState = GetGameSpeed();
            
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

        public static SpeedState GetGameSpeed()
        {
            if (SpeedControlScreen.Instance == null)
            {
                DebugConsole.LogWarning("[GameSpeedSyncer][GetGameSpeed] SpeedControlScreen is not available. default to paused.");
                return SpeedState.Paused;
            }

            if (SpeedControlScreen.Instance.IsPaused)
                return SpeedState.Paused;

            return (SpeedState)SpeedControlScreen.Instance.GetSpeed();
        }

        public void RequestSetSpeed(SpeedState speed)
        {
            try
            {
                if (ENABLE_LOG)
                    DebugConsole.Log(
                        $"[GameSpeedSyncer][RequestSetSpeed] {_authoritativeState} -> {speed}, isApplying: {IsApplyingNetworkState}");

                // The game itself would call to set to same speed, we can ignore those.
                if (IsStateSynchronized(speed))
                    return;

                CallCommand(nameof(CmdSetSpeed), speed);

                if (ENABLE_LOG)
                    DebugConsole.Log($"[GameSpeedSyncer][RequestSetSpeed] request sent. Current: {_authoritativeState}.");
            }
            catch (System.Exception ex)
            {
                DebugConsole.LogError($"[GameSpeedSyncer][RequestSetSpeed] {ex}");
            }
        }

        [Command(SendMode = (int)PacketSendMode.ReliableImmediate)]
        private void CmdSetSpeed(SpeedState speed)
        {
            if (!isServer)
                return;
            
            if (!IsValidSpeed(speed))
                return;

            if (ENABLE_LOG)
                DebugConsole.Log(
                    $"[GameSpeedSyncer][CmdSetSpeed] {_authoritativeState} -> {speed}, " +
                    $"isApplying: {IsApplyingNetworkState}, currentRevision: {_revision}");

            try
            {
                if (!ApplySpeed(speed))
                    return;

                _revision++;
                _lastAppliedRevision = _revision;
                
                if (MultiplayerSession.SessionHasPlayers)
                    CallClientRpc(nameof(RpcApplySpeed), _revision, speed);

                if (ENABLE_LOG)
                    DebugConsole.Log(
                        $"[GameSpeedSyncer][CmdSetSpeed] complete setting speed. " +
                        $"CurrentState: {_authoritativeState}, currentRevision: {_revision}.");
            }
            catch (System.Exception ex)
            {
                DebugConsole.LogError(
                    $"[GameSpeedSyncer][CmdSetSpeed] Failed to set speed to {speed}, " +
                    $"isApplying: {IsApplyingNetworkState}, currentRevision: {_revision}. {ex}");
            }
        }

        [ClientRpc(SendMode = (int)PacketSendMode.ReliableImmediate)]
        private void RpcApplySpeed(uint revision, SpeedState state)
        {
            if (!IsValidSpeed(state))
                return;

            if (ENABLE_LOG)
                DebugConsole.Log(
                    $"[GameSpeedSyncer][RpcApplySpeed] " +
                    $"{_authoritativeState} -> {state}, " +
                    $"revision: {_lastAppliedRevision} -> {revision}, " +
                    $"isApplying: {IsApplyingNetworkState}.");

            if (revision <= _lastAppliedRevision)
                return;

            if (!ApplySpeed(state))
                return;

            _lastAppliedRevision = revision;
            
            if (ENABLE_LOG)
                DebugConsole.Log($"[GameSpeedSyncer][RpcApplySpeed] RPC complete applying speed. Current: {_authoritativeState}.");
        }

        private bool ApplySpeed(SpeedState state)
        {
            _applyDepth++;
            try
            {
                SpeedControlScreen screen = SpeedControlScreen.Instance;
                if (screen == null)
                    return false;

                if (ENABLE_LOG)
                    DebugConsole.LogNonImportant(
                        $"[GameSpeedSyncer][ApplySpeed] " +
                        $"[{_authoritativeState} -> {state}, " +
                        $"isApplying: {IsApplyingNetworkState}, " +
                        $"isGamePaused: {screen.IsPaused}, " +
                        $"gameSpeed: {screen.GetSpeed()}, "
                    );

                // The caller may already have changed the local vanilla state
                // before requesting synchronization (e.g. TogglePause postfix).
                // Accept the state without executing vanilla behavior again.
                if (GetGameSpeed() == state)
                {
                    _authoritativeState = state;
                    return true;
                }

                if (state == SpeedState.Paused)
                {
                    if (!screen.IsPaused)
                        screen.TogglePause();
                }
                else
                {
                    screen.SetSpeed((int)state);
                    if (screen.IsPaused)
                        screen.TogglePause();
                }

                // Respect vanilla's pause counter. A single TogglePause may not
                // actually unpause the game if multiple pause requests are active.
                if (GetGameSpeed() != state)
                {
                    if (ENABLE_LOG)
                    {
                        DebugConsole.Log(
                            $"[GameSpeedSyncer][ApplySpeed] " +
                            $"Could not reach requested state {state}. " +
                            $"Actual state: {GetGameSpeed()}.");
                    }

                    return false;
                }

                if (ENABLE_LOG)
                    DebugConsole.LogNonImportant($"[GameSpeedSyncer][ApplySpeed] Applied speed {state}, origin: {_authoritativeState}.");

                _authoritativeState = state;
                return true;
            }
            catch (System.Exception ex)
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

        public bool IsStateSynchronized(SpeedState state)
        {
            return _authoritativeState == state && GetGameSpeed() == state;
        }

        public static bool IsValidSpeed(SpeedState state)
        {
            return state == SpeedState.Paused
                || state == SpeedState.Normal
                || state == SpeedState.Double
                || state == SpeedState.Triple;
        }
    }
}
