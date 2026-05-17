using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.World;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Components
{
    public class BuildingSyncer : MonoBehaviour
    {
        public static BuildingSyncer Instance { get; private set; }

        private const float SYNC_INTERVAL = 30f;
        private float _lastSyncTime;

        private bool _initialized = false;
        private float _initializationTime;
        private const float INITIAL_DELAY = 5f;

        private void Awake()
        {
            using var _ = Profiler.Scope();

            Instance = this;
        }

        private void Update()
        {
            using var _ = Profiler.Scope();

            if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost)
                return;

            if (MultiplayerSession.ConnectedPlayers.Count == 0)
                return;

            if (!_initialized)
            {
                _initializationTime = Time.unscaledTime;
                _initialized = true;
                return;
            }

            if (Time.unscaledTime - _initializationTime < INITIAL_DELAY)
                return;

            if (Time.unscaledTime - _lastSyncTime > SYNC_INTERVAL)
            {
                _lastSyncTime = Time.unscaledTime;
                SendSyncPacket();
            }

        }

        private void SendSyncPacket()
        {
            using var _ = Profiler.Scope();

            var buildings = global::Components.BuildingCompletes.Items;
            var stateList = new List<BuildingState>(buildings.Count);

            foreach (var building in buildings)
            {
                if (building == null) continue;

                int cell = Grid.PosToCell(building);
                if (!Grid.IsValidCell(cell)) continue;

                var kpid = building.GetComponent<KPrefabID>();
                if (kpid == null) continue;

                stateList.Add(new BuildingState
                {
                    Cell = cell,
                    PrefabName = kpid.PrefabTag.Name
                });
            }

            var packet = new BuildingStatePacket
            {
                Buildings = stateList
            };

            PacketSender.SendToAllClients(packet, PacketSendMode.Unreliable);
        }

        public void OnPacketReceived(BuildingStatePacket packet)
        {
            using var _ = Profiler.Scope();

            if (MultiplayerSession.IsHost) return;
            if (Grid.WidthInCells == 0) return;

            StartCoroutine(Reconcile(packet.Buildings));
        }

        private IEnumerator Reconcile(List<BuildingState> remoteBuildings)
        {
            using var _ = Profiler.Scope();

            // Build remote lookup: (Cell, Layer) -> Prefab
            var remoteByCellLayer = new Dictionary<(int, ObjectLayer), string>();
            foreach (var b in remoteBuildings)
            {
                if (!string.IsNullOrEmpty(b.PrefabName))
                {
                    var def = Assets.GetBuildingDef(b.PrefabName);
                    if (def != null)
                    {
                        remoteByCellLayer[(b.Cell, def.TileLayer)] = b.PrefabName;
                    }
                }
            }

            var localBuildings = global::Components.BuildingCompletes.Items;
            var localList = new List<BuildingComplete>(localBuildings);

            // Replace buildings ONLY if something different exists at same cell AND same layer
            foreach (var building in localList)
            {
                if (building == null) continue;

                ObjectLayer layer = building.Def.TileLayer;
                int cell = Grid.PosToCell(building);
                var kpid = building.GetComponent<KPrefabID>();
                if (kpid == null) continue;

                string localPrefab = kpid.PrefabTag.Name;

                if (remoteByCellLayer.TryGetValue((cell, layer), out var remotePrefab))
                {
                    if (remotePrefab != localPrefab)
                    {
                        DebugConsole.Log($"[BuildingSyncer] Replacing {localPrefab} with {remotePrefab} at {cell} (Layer: {layer})");
                        Util.KDestroyGameObject(building.gameObject);
                    }
                }
                // If no remote building at this cell+layer then do nothing
            }

            // Build a set of (cell, layer, prefab) for existing local buildings
            var localSet = new HashSet<(int, ObjectLayer, string)>();
            foreach (var building in global::Components.BuildingCompletes.Items)
            {
                if (building == null) continue;

                int cell = Grid.PosToCell(building);
                var kpid = building.GetComponent<KPrefabID>();
                if (kpid == null) continue;

                localSet.Add((cell, building.Def.TileLayer, kpid.PrefabTag.Name));
            }

            // Spawn missing remote buildings
            foreach (var remote in remoteBuildings)
            {
                if (string.IsNullOrEmpty(remote.PrefabName)) continue;

                var def = Assets.GetBuildingDef(remote.PrefabName);
                if (def == null) continue;

                if (!localSet.Contains((remote.Cell, def.TileLayer, remote.PrefabName)))
                {
                    DebugConsole.Log($"[BuildingSyncer] Spawning missing building {remote.PrefabName} at {remote.Cell} (Layer: {def.TileLayer})");
                    SpawnBuilding(remote.Cell, remote.PrefabName);
                    yield return null;
                }
            }
        }

        private void SpawnBuilding(int cell, string prefabName)
        {
            using var _ = Profiler.Scope();

            if (Grid.WidthInCells == 0) return;
            if (string.IsNullOrEmpty(prefabName)) return;

            var def = Assets.GetBuildingDef(prefabName);

            if (def == null)
            {
                GameObject prefab = Assets.GetPrefab(prefabName);
                if (prefab != null)
                {
                    var wBuilding = prefab.GetComponent<Building>();
                    if (wBuilding != null) def = wBuilding.Def;
                }
            }

            if (def != null)
            {
                try
                {
                    Vector3 pos = Grid.CellToPosCBC(cell, def.SceneLayer);
                    GameObject go = Util.KInstantiate(Assets.GetPrefab(def.Tag), pos);

                    if (go != null)
                    {
                        var primaryElement = go.GetComponent<PrimaryElement>();
                        if (primaryElement != null)
                        {
                            var safeElement = ElementLoader.FindElementByHash(SimHashes.SandStone)
                                ?? ElementLoader.FindElementByHash(SimHashes.Dirt)
                                ?? ElementLoader.elements?.FirstOrDefault(e => e.IsSolid);

                            if (safeElement != null)
                            {
                                primaryElement.SetElement(safeElement.id, true);
                                primaryElement.Temperature = 293.15f;
                                if (primaryElement.Mass <= 0.001f)
                                    primaryElement.Mass = 100f;
                            }
                        }

                        go.SetActive(true);
                    }
                }
                catch (System.Exception ex)
                {
                    DebugConsole.LogError($"[BuildingSyncer] Failed to spawn building {def.Name} at {cell}: {ex}");
                }
            }
            else
            {
                DebugConsole.LogWarning($"[BuildingSyncer] Could not find BuildingDef for {prefabName}");
            }
        }
    }
}