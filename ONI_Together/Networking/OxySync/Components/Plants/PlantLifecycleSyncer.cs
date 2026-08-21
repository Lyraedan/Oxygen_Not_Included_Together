using KSerialization;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using Shared.Profiling;
using System.Collections;
using Shared.Helpers;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
    public enum PlantLifecycleOperation : byte
    {
        Spawn = 0,
        Remove = 1,
    }

    public struct PlantData
    {
        public int PlantNetId;
        public int ReceptacleNetId;
        public int Cell;
        public string PlantPrefabTag;
        public float Maturity;
        public bool IsWilting;
        public bool IsHarvestReady;
        public bool IsWild;
    }

    [SkipSaveFileSerialization]
    [FixedInterestGroup]
    public class PlantLifecycleSyncer : NetworkBehaviour
    {
        public static PlantLifecycleSyncer Instance { get; private set; }

        public static bool IsApplyingState = false;

        private const float LIVE_EVENT_DELAY = 2f;

        private bool _initialized;
        private float _initializationTime;

        public static bool CanBroadcast =>
            Instance != null &&
            Instance._initialized &&
            Time.unscaledTime - Instance._initializationTime >= LIVE_EVENT_DELAY &&
            MultiplayerSession.InActiveSession &&
            MultiplayerSession.IsHost &&
            MultiplayerSession.ConnectedPlayers.Count > 0 &&
            !GameServerHardSync.IsHardSyncInProgress;

        public override void OnSpawn()
        {
            base.OnSpawn();
            Instance = this;
            InterestGroup = -1;
            SetNetworkIdentity();
            _initializationTime = Time.unscaledTime;
            _initialized = true;
        }

        public void SetNetworkIdentity()
        {
            NetId = nameof(PlantLifecycleSyncer).GetHashCode();
        }

        public override void OnCleanUp()
        {
            if (Instance == this)
                Instance = null;

            base.OnCleanUp();
        }

        public void BroadcastSpawn(Growing growing, SingleEntityReceptacle receptacleOverride = null)
        {
            using var _ = Profiler.Scope();

            if (!CanBroadcast)
                return;

            if (!TryBuildPlantData(growing, out var data, receptacleOverride))
                return;

            CallClientRpc(nameof(RpcSpawnPlant),
                data.PlantNetId, data.ReceptacleNetId, data.Cell,
                data.PlantPrefabTag ?? string.Empty,
                data.Maturity, data.IsWilting, data.IsHarvestReady, data.IsWild);
        }

        public void BroadcastRemove(Growing growing)
        {
            using var _ = Profiler.Scope();

            if (!CanBroadcast)
                return;

            if (!TryBuildPlantData(growing, out var data))
                return;

            CallClientRpc(nameof(RpcRemovePlant),
                data.PlantNetId, data.ReceptacleNetId, data.Cell,
                data.PlantPrefabTag ?? string.Empty,
                data.Maturity, data.IsWilting, data.IsHarvestReady, data.IsWild);
        }

        [ClientRpc(SendMode = (int)PacketSendMode.ReliableImmediate)]
        private void RpcSpawnPlant(int plantNetId, int receptacleNetId, int cell,
            string plantPrefabTag, float maturity, bool isWilting, bool isHarvestReady, bool isWild)
        {
            using var _ = Profiler.Scope();

            if (MultiplayerSession.IsHost || Grid.WidthInCells == 0)
                return;

            var data = new PlantData
            {
                PlantNetId = plantNetId,
                ReceptacleNetId = receptacleNetId,
                Cell = cell,
                PlantPrefabTag = plantPrefabTag,
                Maturity = maturity,
                IsWilting = isWilting,
                IsHarvestReady = isHarvestReady,
                IsWild = isWild
            };

            if (HandleSpawnPlant(data))
                return;

            if (Game.Instance != null)
                Game.Instance.StartCoroutine(RetrySpawnPlant(data));
        }

        [ClientRpc(SendMode = (int)PacketSendMode.ReliableImmediate)]
        private void RpcRemovePlant(int plantNetId, int receptacleNetId, int cell,
            string plantPrefabTag, float maturity, bool isWilting, bool isHarvestReady, bool isWild)
        {
            using var _ = Profiler.Scope();

            if (MultiplayerSession.IsHost || Grid.WidthInCells == 0)
                return;

            var data = new PlantData
            {
                PlantNetId = plantNetId,
                ReceptacleNetId = receptacleNetId,
                Cell = cell,
                PlantPrefabTag = plantPrefabTag,
                Maturity = maturity,
                IsWilting = isWilting,
                IsHarvestReady = isHarvestReady,
                IsWild = isWild
            };

            if (HandleRemovePlant(data))
                return;

            if (Game.Instance != null)
                Game.Instance.StartCoroutine(RetryRemovePlant(data));
        }

        private static IEnumerator RetrySpawnPlant(PlantData data)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                yield return null;

                if (!MultiplayerSession.InActiveSession || MultiplayerSession.IsHost)
                    yield break;

                if (HandleSpawnPlant(data))
                    yield break;
            }

            DebugConsole.LogWarning($"[PlantLifecycle] Failed to spawn plant '{data.PlantPrefabTag}' at cell {data.Cell}");
        }

        private static IEnumerator RetryRemovePlant(PlantData data)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                yield return null;

                if (!MultiplayerSession.InActiveSession || MultiplayerSession.IsHost)
                    yield break;

                if (HandleRemovePlant(data))
                    yield break;
            }

            DebugConsole.LogWarning($"[PlantLifecycle] Failed to remove plant '{data.PlantPrefabTag}' at cell {data.Cell}");
        }

        private static bool HandleSpawnPlant(PlantData data)
        {
            using var _ = Profiler.Scope();

            try
            {
                IsApplyingState = true;
                return SpawnOrUpdatePlant(data);
            }
            catch (System.Exception ex)
            {
                DebugConsole.LogError($"[PlantLifecycle] Error spawning plant at cell {data.Cell}: {ex.Message}");
                return false;
            }
            finally
            {
                IsApplyingState = false;
            }
        }

        private static bool HandleRemovePlant(PlantData data)
        {
            using var _ = Profiler.Scope();

            try
            {
                IsApplyingState = true;
                return RemovePlant(data);
            }
            catch (System.Exception ex)
            {
                DebugConsole.LogError($"[PlantLifecycle] Error removing plant at cell {data.Cell}: {ex.Message}");
                return false;
            }
            finally
            {
                IsApplyingState = false;
            }
        }

        public static bool TryBuildPlantData(Growing growing, out PlantData data, SingleEntityReceptacle receptacleOverride = null)
        {
            using var _ = Profiler.Scope();

            data = default;

            if (growing == null || growing.gameObject == null)
                return false;

            int cell = Grid.PosToCell(growing.gameObject);
            if (!Grid.IsValidCell(cell))
                return false;

            if (!growing.TryGetComponent<KPrefabID>(out var kpid) || kpid == null)
                return false;

            int plantNetId = EnsureIdentity(growing.gameObject, 0);
            int receptacleNetId = 0;
            bool isWild = growing.IsWildPlanted();

            var receptacle = receptacleOverride;
            if (receptacle == null)
            {
                TryGetReceptacle(growing, out receptacle);
            }

            if (receptacle != null && receptacle.gameObject != null)
            {
                receptacleNetId = EnsureIdentity(receptacle.gameObject, 0);
                isWild = false;
            }

            bool isWilting = false;
            if (growing.TryGetComponent<WiltCondition>(out var wiltCondition) && wiltCondition != null)
            {
                isWilting = wiltCondition.IsWilting();
            }

            bool isHarvestReady = false;
            if (growing.TryGetComponent<HarvestDesignatable>(out var harvestDesignatable) && harvestDesignatable != null)
            {
                isHarvestReady = harvestDesignatable.CanBeHarvested();
            }

            data = new PlantData
            {
                PlantNetId = plantNetId,
                ReceptacleNetId = receptacleNetId,
                Cell = cell,
                PlantPrefabTag = kpid.PrefabTag.Name,
                Maturity = growing.PercentGrown(),
                IsWilting = isWilting,
                IsHarvestReady = isHarvestReady,
                IsWild = isWild
            };
            return true;
        }

        private static bool SpawnOrUpdatePlant(PlantData data)
        {
            using var _ = Profiler.Scope();

            if (!Grid.IsValidCell(data.Cell) || string.IsNullOrEmpty(data.PlantPrefabTag))
                return false;

            if (TryFindLocalPlant(data, out var existingPlant))
            {
                ApplyPlantState(existingPlant, data);
                return true;
            }

            var receptacle = ResolveReceptacle(data);
            if (receptacle != null && receptacle.Occupant != null && receptacle.Occupant.TryGetComponent<Growing>(out var occupantPlant))
            {
                bool samePlant = GetExistingIdentityId(occupantPlant.gameObject) == data.PlantNetId;
                if (!samePlant &&
                    occupantPlant.TryGetComponent<KPrefabID>(out var occupantKpid) &&
                    occupantKpid != null &&
                    string.Equals(occupantKpid.PrefabTag.Name, data.PlantPrefabTag))
                {
                    samePlant = true;
                }

                if (samePlant)
                {
                    ApplyPlantState(occupantPlant, data);
                    return true;
                }

                Util.KDestroyGameObject(receptacle.Occupant);
            }

            var prefab = Assets.GetPrefab(new Tag(data.PlantPrefabTag));
            if (prefab == null)
            {
                DebugConsole.LogWarning($"[PlantLifecycle] Could not find prefab for plant '{data.PlantPrefabTag}'");
                return false;
            }

            Vector3 pos = Grid.CellToPosCBC(data.Cell, Grid.SceneLayer.BuildingFront);
            GameObject plantGo = Util.KInstantiate(prefab, pos);
            if (plantGo == null)
                return false;

            plantGo.SetActive(true);
            EnsureIdentity(plantGo, data.PlantNetId);

            if (receptacle is PlantablePlot plot)
            {
                plot.ReplacePlant(plantGo, true);

                if (plantGo.TryGetComponent<ReceptacleMonitor>(out var rm) && rm != null)
                {
                    rm.SetReceptacle(plot);
                }
            }
            else if (receptacle != null)
            {
                receptacle.CancelActiveRequest();
                receptacle.ForceDeposit(plantGo);
            }

            if (!TryFindLocalPlant(data, out var spawnedPlant))
            {
                spawnedPlant = plantGo.GetComponent<Growing>();
            }

            if (spawnedPlant == null)
            {
                DebugConsole.LogWarning($"[PlantLifecycle] Spawned plant '{data.PlantPrefabTag}' but could not resolve Growing at cell {data.Cell}");
                return false;
            }

            ApplyPlantState(spawnedPlant, data);

            DebugConsole.Log(receptacle != null
                ? $"[PlantLifecycle] Spawned planted crop '{data.PlantPrefabTag}' at cell {data.Cell} for receptacle {data.ReceptacleNetId}"
                : $"[PlantLifecycle] Spawned wild plant '{data.PlantPrefabTag}' at cell {data.Cell}");

            return true;
        }

        private static bool RemovePlant(PlantData data)
        {
            using var _ = Profiler.Scope();

            if (TryFindLocalPlant(data, out var growing) && growing != null && growing.gameObject != null)
            {
                Util.KDestroyGameObject(growing.gameObject);
                DebugConsole.Log($"[PlantLifecycle] Removed plant '{data.PlantPrefabTag}' at cell {data.Cell}");
                return true;
            }

            var receptacle = ResolveReceptacle(data);
            if (receptacle?.Occupant != null)
            {
                Util.KDestroyGameObject(receptacle.Occupant);
                DebugConsole.Log($"[PlantLifecycle] Removed receptacle occupant for plant '{data.PlantPrefabTag}' at cell {data.Cell}");
                return true;
            }

            return false;
        }

        private static bool TryFindLocalPlant(PlantData data, out Growing growing)
        {
            using var _ = Profiler.Scope();

            growing = null;

            if (data.PlantNetId != 0 && NetworkIdentityRegistry.TryGetComponent(data.PlantNetId, out Growing byId) && byId != null)
            {
                growing = byId;
                return true;
            }

            if (data.ReceptacleNetId != 0 &&
                NetworkIdentityRegistry.TryGet(data.ReceptacleNetId, out var receptacleIdentity) &&
                receptacleIdentity != null &&
                receptacleIdentity.gameObject.TryGetComponent<SingleEntityReceptacle>(out var receptacle) &&
                receptacle.Occupant != null &&
                receptacle.Occupant.TryGetComponent<Growing>(out var byReceptacle) &&
                byReceptacle != null)
            {
                growing = byReceptacle;
                return true;
            }

            return false;
        }

        private static SingleEntityReceptacle ResolveReceptacle(PlantData data)
        {
            using var _ = Profiler.Scope();

            if (data.ReceptacleNetId != 0 &&
                NetworkIdentityRegistry.TryGet(data.ReceptacleNetId, out var receptacleIdentity) &&
                receptacleIdentity != null &&
                receptacleIdentity.gameObject.TryGetComponent<SingleEntityReceptacle>(out var byId))
            {
                return byId;
            }

            if (!Grid.IsValidCell(data.Cell))
                return null;

            ObjectLayer[] layersToCheck =
            {
                ObjectLayer.Building,
                ObjectLayer.FoundationTile,
                ObjectLayer.Plants,
                ObjectLayer.AttachableBuilding,
            };

            foreach (var layer in layersToCheck)
            {
                var obj = Grid.Objects[data.Cell, (int)layer];
                if (obj != null && obj.TryGetComponent<SingleEntityReceptacle>(out var receptacle))
                    return receptacle;
            }

            return null;
        }

        private static bool TryGetReceptacle(Growing growing, out SingleEntityReceptacle receptacle)
        {
            using var _ = Profiler.Scope();

            receptacle = null;
            if (growing == null || growing.gameObject == null)
                return false;

            if (growing.TryGetComponent<ReceptacleMonitor>(out var rm) && rm != null)
            {
                receptacle = rm.GetReceptacle();
                if (receptacle != null)
                    return true;
            }

            receptacle = growing.GetComponentInParent<SingleEntityReceptacle>();
            return receptacle != null;
        }

        private static int EnsureIdentity(GameObject go, int targetNetId)
        {
            using var _ = Profiler.Scope();

            if (go == null)
                return 0;

            if (targetNetId != 0)
                return NetIdentityHelper.OverrideNetId(go, targetNetId);

            return NetIdentityHelper.AddOrGetNetId(go, 0);
        }

        private static int GetExistingIdentityId(GameObject go)
        {
            using var _ = Profiler.Scope();

            if (go == null || !go.TryGetComponent<NetworkIdentity>(out var identity) || identity == null)
                return 0;

            return identity.NetId;
        }

        private static void ApplyPlantState(Growing growing, PlantData data)
        {
            using var _ = Profiler.Scope();

            if (growing == null || growing.gameObject == null)
                return;

            try
            {
                EnsureIdentity(growing.gameObject, data.PlantNetId);

                float currentMaturity = growing.PercentGrown();
                if (Mathf.Abs(currentMaturity - data.Maturity) > 0.001f)
                {
                    growing.OverrideMaturityLevel(data.Maturity);
                }

                if (!data.IsWild && TryGetReceptacle(growing, out var receptacle) && receptacle is PlantablePlot plot)
                {
                    if (growing.TryGetComponent<ReceptacleMonitor>(out var rm) && rm != null)
                    {
                        rm.SetReceptacle(plot);
                    }
                }

                if (growing.TryGetComponent<WiltCondition>(out var wiltCondition) && wiltCondition != null)
                {
                    if (data.IsWilting && !wiltCondition.IsWilting())
                    {
                        wiltCondition.DoWilt();
                    }
                    else if (!data.IsWilting && wiltCondition.IsWilting())
                    {
                        wiltCondition.DoRecover();
                    }
                }

                if (growing.TryGetComponent<KBatchedAnimController>(out var kbac) && kbac != null)
                {
                    kbac.SetVisiblity(true);
                    kbac.forceRebuild = true;
                }
            }
            catch (System.Exception ex)
            {
                DebugConsole.LogError($"[PlantLifecycle] Error applying plant state at cell {data.Cell}: {ex.Message}");
            }
        }
    }
}
