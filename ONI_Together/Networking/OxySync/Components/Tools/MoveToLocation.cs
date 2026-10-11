using System.Collections.Generic;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components.Tools
{
    [SkipSaveFileSerialization]
    [FixedInterestGroup]
    public class MoveToLocationToolSyncer : NetworkBehaviour
    {
        public static bool GetTargetNetIdFromMoveToLocationTool(MoveToLocationTool tool, out int netId)
        {
            netId = 0;
            if (tool == null || tool.gameObject == null || tool.gameObject.IsNullOrDestroyed())
            {
                DebugConsole.LogWarning("[MoveToLocationToolSyncer] MoveToLocationTool instance is null or destroyed.");
                return false;
            }

            var go = tool.targetNavigator?.gameObject ?? tool.targetMovable?.gameObject;
            if (go != null && go.TryGetComponent<NetworkIdentity>(out var identity))
            {
                netId = identity.NetId;
                return true;
            }

            return false;
        }

        private void MoveToLocation(int targetNetId, int targetCell)
        {
            DebugConsole.Log($"[MoveToLocationToolSyncer] RpcMoveToLocation sync to move targetNetId {targetNetId} to cell {targetCell}");
            if (NetworkIdentityRegistry.TryGet(targetNetId, out var go) && go != null)
			{
                if (isClient && MultiplayerSession.IsClient) {
                    Instance.moveToAuthorization.Add((targetNetId, targetCell));
                }

                if (go.TryGetComponent(out Navigator nav) && nav != null)
                {
                    nav.GetSMI<MoveToLocationMonitor.Instance>()?.MoveToLocation(targetCell);
                    DebugConsole.Log($"[MoveToLocationToolSyncer] Navigator with NetId {targetNetId} moved to {targetCell}");
                    return;
                }

                if (go.TryGetComponent(out Movable movable) && movable != null)
                {
                    movable.MoveToLocation(targetCell);
                    DebugConsole.Log($"[MoveToLocationToolSyncer] Movable with NetId {targetNetId} moved to {targetCell}");
                    return;
                }
            }

            DebugConsole.LogWarning($"[MoveToLocationToolSyncer] No Navigator/Movable found on entity with NetId {targetNetId}");
        }

        public static MoveToLocationToolSyncer Instance { get; private set; }

        private readonly HashSet<(int TargetNetId, int TargetCell)> moveToAuthorization = [];

        public override void OnSpawn()
        {
            base.OnSpawn();
            Instance = this;
            InterestGroup = -1;
        }

        public override void OnCleanUp()
        {
            if (Instance == this)
                Instance = null;
            base.OnCleanUp();
        }

        public static void RegisterNetId(GameObject parent = null) {
            var root = parent == null ? Game.Instance.gameObject : parent;
            if (Instance == null)
            {
                DebugConsole.LogWarning("[MoveToLocationToolSyncer] Initializing MoveToLocationToolSyncer instance.");
                var syncerRoot = new GameObject("MoveToLocationToolSyncer");
                syncerRoot.transform.SetParent(root.transform);
                Instance = syncerRoot.AddComponent<MoveToLocationToolSyncer>();
            }

            Instance.moveToAuthorization.Clear();
            Instance.NetId = nameof(MoveToLocationToolSyncer).GetHashCode();
        }

        public static bool IsAuthorized(int targetNetId, int targetCell)
        {
            var syncer = Instance;
            if (syncer == null)
            {
                return false;
            }

            return syncer.moveToAuthorization.Contains((targetNetId, targetCell));
        }

        public static void UnAuthorize(int targetNetId, int targetCell)
        {
            Instance?.moveToAuthorization.Remove((targetNetId, targetCell));
        }

        public static void RequestCallMoveToLocation(int targetNetId, int targetCell)
        {
            var syncer = Instance;
            if (syncer == null)
            {
                DebugConsole.LogWarning($"[MoveToLocationToolSyncer] Skip sync for MoveTo Tool syncer missing. IsHostInSession={MultiplayerSession.IsHostInSession}, SyncerNull={syncer == null}");
                return;
            }

            try
            {
                DebugConsole.Log($"[MoveToLocationToolSyncer] Sync MoveTo Tool with targetNetId {targetNetId} to cell {targetCell}");
                var cmdName = MultiplayerSession.IsHostInSession ? nameof(CmdClientMoveToLocation) : nameof(CmdHostMoveToLocation);
                syncer.CallCommand(cmdName, targetNetId, targetCell);
            }
            catch (System.Exception ex)
            {
                DebugConsole.LogWarning($"[MoveToLocationToolSyncer] Failed to request moving targetNetId {targetNetId} to cell {targetCell}. Error: {ex}");
            }
        }

        [Command]
        private void CmdClientMoveToLocation(int targetNetId, int targetCell)
        {
            CallClientRpc(nameof(RpcMoveToLocation), targetNetId, targetCell);
        }

        [Command]
        private void CmdHostMoveToLocation(int targetNetId, int targetCell)
        {
            MoveToLocation(targetNetId, targetCell);

            // MoveToLocation would not trigger the MoveToLocationTool.SetMoveToLocation Prifix. Directly call to sync
            CallClientRpc(nameof(RpcMoveToLocation), targetNetId, targetCell);
        }

        [ClientRpc]
        private void RpcMoveToLocation(int targetNetId, int targetCell)
        {
            MoveToLocation(targetNetId, targetCell);
        }
    }
}
