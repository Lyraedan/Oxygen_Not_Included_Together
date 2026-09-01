using System.Collections.Generic;
using System;
using System.Linq;
using ONI_Together.DebugTools;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    [FixedInterestGroup]
    public class WorkableSyncer : NetworkBehaviour
    {

        private string GetWorkableTypeId(Workable workable)
        {
            return workable?.GetType().AssemblyQualifiedName ?? string.Empty;
        }

        private string GetWorkableTypeId(string workableTypeId)
        {
            return workableTypeId ?? string.Empty;
        }

        private Type ResolveWorkableType(string workableTypeId)
        {
            string normalizedTypeId = GetWorkableTypeId(workableTypeId);
            if (string.IsNullOrEmpty(normalizedTypeId))
            {
                return null;
            }

            var type = Type.GetType(normalizedTypeId);
            if (type != null)
            {
                return type;
            }

            string fullName = normalizedTypeId.Split(',')[0].Trim();
            if (string.IsNullOrEmpty(fullName))
            {
                return null;
            }

            return AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(asm => asm.GetType(fullName, throwOnError: false, ignoreCase: false))
                .FirstOrDefault(t => t != null);
        }

        private (int WorkableNetId, string WorkableTypeId, MethodType Method) BuildAuthKey(int workableNetId, string workableTypeId, MethodType method)
        {
            return (workableNetId, GetWorkableTypeId(workableTypeId), method);
        }

        private (int WorkableNetId, string WorkableTypeId, MethodType Method) BuildAuthKey(Workable workable, MethodType method)
        {
            return (workable.GetNetId(), GetWorkableTypeId(workable), method);
        }

        public static WorkableSyncer Instance { get; private set; }
        public enum MethodType: byte
        {
            StartWork,
            StopWork,
            CompleteWork,
            AbortWork
        }

        private Dictionary<(int WorkableNetId, string WorkableTypeId, MethodType Method), int> workableAuthorization = [];

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
                DebugConsole.LogWarning("[WorkableSyncer] Initializing WorkableSyncer instance.");
                var syncerRoot = new GameObject("WorkableSyncer");
                syncerRoot.transform.SetParent(root.transform);
                Instance = syncerRoot.AddComponent<WorkableSyncer>();
            }

            Instance.workableAuthorization.Clear();
            Instance.NetId = nameof(WorkableSyncer).GetHashCode();
        }

        public static bool IsAuthorized(int workableNetId, string workableTypeId, MethodType method)
        {
            if (Instance == null)
            {
                return false;
            }

            return Instance.workableAuthorization.ContainsKey(Instance.BuildAuthKey(workableNetId, workableTypeId, method));
        }

        public static bool IsAuthorized(int workableNetId, string workableTypeId, MethodType method, out int workerNetId)
        {
            var syncer = Instance;
            if (syncer != null && syncer.workableAuthorization.TryGetValue(syncer.BuildAuthKey(workableNetId, workableTypeId, method), out var workerId))
            {
                workerNetId = workerId;
                return true;
            }

            workerNetId = 0;
            return false;
        }

        public static bool IsAuthorized(Workable workable, MethodType method)
        {
            var syncer = Instance;
            if (syncer == null || workable == null || workable.IsNullOrDestroyed())
            {
                return false;
            }

            return syncer.workableAuthorization.ContainsKey(syncer.BuildAuthKey(workable, method));
        }

        public static bool IsAuthorized(Workable workable, MethodType method, out int workerNetId)
        {
            var syncer = Instance;
            if (syncer != null && workable != null && !workable.IsNullOrDestroyed() &&
                syncer.workableAuthorization.TryGetValue(syncer.BuildAuthKey(workable, method), out var workerId))
            {
                workerNetId = workerId;
                return true;
            }

            workerNetId = 0;
            return false;
        }

        public static void UnAuthorize(int workableNetId, string workableTypeId, MethodType method)
        {
            Instance?.workableAuthorization.Remove(Instance.BuildAuthKey(workableNetId, workableTypeId, method));
        }

        public static void UnAuthorize(Workable workable, MethodType method)
        {
            if (workable == null || workable.IsNullOrDestroyed())
            {
                return;
            }

            Instance?.workableAuthorization.Remove(Instance.BuildAuthKey(workable, method));
        }

        public void RequestUpdateWorkable(MethodType method, Workable workable, WorkerBase worker)
        {
            if (!MultiplayerSession.IsHostInSession || !MultiplayerSession.SessionHasPlayers)
            {
                return;
            }

            if (workable.IsNullOrDestroyed() || worker.IsNullOrDestroyed())
            {
                DebugConsole.LogWarning($"[WorkableSyncer] Skip sync for method {method}: WorkableNullOrDestroyed={workable.IsNullOrDestroyed()}, WorkerNullOrDestroyed={worker.IsNullOrDestroyed()}");
                return;
            }

            int workableNetId = workable.GetNetId();
            int workerNetId = worker.GetNetId();
            if (workableNetId == 0 || workerNetId == 0)
            {
                DebugConsole.LogWarning(
                    $"[WorkableSyncer] Skip sync for method {method}: invalid NetId(s). " +
                    $"Workable '{workable.GetProperName()}' has NetId '{workableNetId}', " +
                    $"Worker '{worker.GetProperName()}' has NetId '{workerNetId}'");

                return;
            }

            try
            {
                string workableTypeId = GetWorkableTypeId(workable);
                DebugConsole.Log($"[WorkableSyncer] Worker has NetId {workerNetId} '{method}' on workable {workableNetId} : {workableTypeId}");
                CallClientRpc(nameof(RpcUpdateWorkable), method, workableNetId, workableTypeId, workerNetId);
            }
            catch (System.Exception ex)
            {
                DebugConsole.LogWarning($"[WorkableSyncer] Failed to request update for method {method}. WorkableNetId={workableNetId}, WorkerNetId={workerNetId}. Error: {ex}");
            }
        }

        [ClientRpc]
        private void RpcUpdateWorkable(MethodType method, int workableNetId, string workableTypeId, int workerNetId)
        {
            if (workableNetId == 0 || !NetworkIdentityRegistry.TryGet(workableNetId, out var identity) || identity == null || identity.gameObject.IsNullOrDestroyed())
            {
                return;
            }

            Workable workable = null;
            string normalizedTypeId = GetWorkableTypeId(workableTypeId);
            if (!string.IsNullOrEmpty(normalizedTypeId))
            {
                var workableType = ResolveWorkableType(normalizedTypeId);
                if (workableType == null)
                {
                    DebugConsole.LogWarning($"[WorkableSyncer] Could not resolve workable type '{normalizedTypeId}' for netId {workableNetId}");
                    return;
                }

                workable = identity.gameObject.GetComponent(workableType) as Workable;
                if (workable == null)
                {
                    DebugConsole.LogWarning($"[WorkableSyncer] GameObject for netId {workableNetId} does not have workable type '{normalizedTypeId}'");
                    return;
                }
            }

            workable ??= identity.gameObject.GetComponent<Workable>();
            if (workable == null)
            {
                return;
            }

            if (workerNetId == 0 || !NetworkIdentityRegistry.TryGetComponent<WorkerBase>(workerNetId, out var worker) || worker == null || worker.gameObject.IsNullOrDestroyed())
            {
                return;
            }

            workableAuthorization[BuildAuthKey(workableNetId, workableTypeId, method)] = workerNetId;

            DebugConsole.Log($"[WorkableSyncer] [Client] Worker has NetId {workerNetId} '{method}' on workable {workableNetId} : {workableTypeId}");

            switch (method)
            {
                case MethodType.StartWork:
                    workable.StartWork(worker);
                    break;
                case MethodType.StopWork:
                    workable.StopWork(worker, false);
                    break;
                case MethodType.CompleteWork:
                    workable.CompleteWork(worker);
                    break;
                case MethodType.AbortWork:
                    workable.StopWork(worker, true);
                    break;
                default:
                    DebugConsole.LogWarning($"[WorkableSyncer] Unknown method name: {method}");
                    break;
            }
        }
    }
}
