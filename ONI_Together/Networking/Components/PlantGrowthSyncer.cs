using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.Trackers;
using Shared.Profiling;
using System.Collections.Generic;
using UnityEngine;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Components
{
	public class PlantGrowthSyncer : MonoBehaviour
	{
		public static PlantGrowthSyncer Instance { get; private set; }

		public static bool IsApplyingState = false;

		private const float SYNC_INTERVAL = 5f;
		private const float INITIAL_DELAY = 7f;
		private const float LIVE_EVENT_DELAY = 2f;

		private float _lastSyncTime;
		private bool _initialized;
		private float _initializationTime;

		private void Awake()
		{
			using var _ = Profiler.Scope();

			Instance = this;
		}

		public static bool CanBroadcastLifecycleEvents =>
			Instance != null &&
			Instance._initialized &&
			Time.unscaledTime - Instance._initializationTime >= LIVE_EVENT_DELAY &&
			MultiplayerSession.InSession &&
			MultiplayerSession.IsHost &&
			MultiplayerSession.ConnectedPlayers.Count > 0 &&
			!GameServerHardSync.IsHardSyncInProgress;

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

			if (Time.unscaledTime - _lastSyncTime <= SYNC_INTERVAL)
				return;

			_lastSyncTime = Time.unscaledTime;
			SendPlantStates();
		}

		public static void BroadcastPlantLifecycle(PlantLifecycleOperation operation, Growing growing, SingleEntityReceptacle receptacleOverride = null)
		{
			using var _ = Profiler.Scope();

			if (!CanBroadcastLifecycleEvents)
				return;

			if (!TryBuildPlantData(growing, out var data, receptacleOverride))
				return;

			PacketSender.SendToAllClients(new PlantLifecyclePacket
			{
				Operation = operation,
				Plant = data
			});
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

		private void SendPlantStates()
		{
			using var _ = Profiler.Scope();

			var sw = System.Diagnostics.Stopwatch.StartNew();
			var packet = new PlantGrowthStatePacket();

			lock (PlantTracker.AllPlants)
			{
				foreach (var growing in PlantTracker.AllPlants)
				{
					if (!TryBuildPlantData(growing, out var data))
						continue;

					packet.Plants.Add(data);
				}
			}

			PacketSender.SendToAllClients(packet, PacketSendMode.Unreliable);

			sw.Stop();
			SyncStats.RecordSync(SyncStats.Plants, packet.Plants.Count, packet.Plants.Count * 48, sw.ElapsedMilliseconds);
		}

		public bool OnPlantLifecycleReceived(PlantLifecyclePacket packet)
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost || Grid.WidthInCells == 0)
				return false;

			try
			{
				IsApplyingState = true;
				return packet.Operation switch
				{
					PlantLifecycleOperation.Spawn => SpawnOrUpdatePlant(packet.Plant),
					PlantLifecycleOperation.Remove => RemovePlant(packet.Plant),
					_ => false
				};
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[PlantGrowthSyncer] Error applying lifecycle packet {packet.Operation}: {ex.Message}");
				return false;
			}
			finally
			{
				IsApplyingState = false;
			}
		}

		public void OnPlantStateReceived(PlantGrowthStatePacket packet)
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost || Grid.WidthInCells == 0)
				return;

			try
			{
				IsApplyingState = true;

				var remoteByPlantId = new Dictionary<int, PlantData>();
				var remoteByReceptacleId = new Dictionary<int, PlantData>();
				var remoteByCell = new Dictionary<int, PlantData>();

				foreach (var plant in packet.Plants)
				{
					if (plant.PlantNetId != 0)
						remoteByPlantId[plant.PlantNetId] = plant;
					if (plant.ReceptacleNetId != 0)
						remoteByReceptacleId[plant.ReceptacleNetId] = plant;
					if (Grid.IsValidCell(plant.Cell))
						remoteByCell[plant.Cell] = plant;
				}

				var matchedPlantIds = new HashSet<int>();
				var matchedReceptacleIds = new HashSet<int>();
				var matchedCells = new HashSet<int>();
				var toRemove = new List<Growing>();

				lock (PlantTracker.AllPlants)
				{
					foreach (var growing in PlantTracker.AllPlants)
					{
						if (growing == null)
							continue;

						if (TryFindMatchingRemote(growing, remoteByPlantId, remoteByReceptacleId, remoteByCell, out var remoteData))
						{
							if (remoteData.PlantNetId != 0)
								matchedPlantIds.Add(remoteData.PlantNetId);
							if (remoteData.ReceptacleNetId != 0)
								matchedReceptacleIds.Add(remoteData.ReceptacleNetId);
							if (Grid.IsValidCell(remoteData.Cell))
								matchedCells.Add(remoteData.Cell);

							ApplyPlantState(growing, remoteData);
							continue;
						}

						toRemove.Add(growing);
					}
				}

				foreach (var growing in toRemove)
				{
					if (growing == null || growing.gameObject == null)
						continue;

					DebugConsole.Log($"[PlantGrowthSyncer] Removing phantom plant at {Grid.PosToCell(growing)}");
					Util.KDestroyGameObject(growing.gameObject);
				}

				foreach (var plant in packet.Plants)
				{
					if (plant.PlantNetId != 0 && matchedPlantIds.Contains(plant.PlantNetId))
						continue;
					if (plant.PlantNetId == 0 && plant.ReceptacleNetId != 0 && matchedReceptacleIds.Contains(plant.ReceptacleNetId))
						continue;
					if (plant.PlantNetId == 0 && plant.ReceptacleNetId == 0 && matchedCells.Contains(plant.Cell))
						continue;

					SpawnOrUpdatePlant(plant);
				}
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[PlantGrowthSyncer] Error in OnPlantStateReceived: {ex.Message}");
			}
			finally
			{
				IsApplyingState = false;
			}
		}

		private static bool TryFindMatchingRemote(
			Growing growing,
			Dictionary<int, PlantData> remoteByPlantId,
			Dictionary<int, PlantData> remoteByReceptacleId,
			Dictionary<int, PlantData> remoteByCell,
			out PlantData data)
		{
			using var _ = Profiler.Scope();

			data = default;

			int plantNetId = GetExistingIdentityId(growing.gameObject);
			if (plantNetId != 0 && remoteByPlantId.TryGetValue(plantNetId, out data))
				return true;

			if (TryGetReceptacle(growing, out var receptacle))
			{
				int receptacleNetId = GetExistingIdentityId(receptacle.gameObject);
				if (receptacleNetId != 0 && remoteByReceptacleId.TryGetValue(receptacleNetId, out data))
					return true;
			}

			int cell = Grid.PosToCell(growing.gameObject);
			return Grid.IsValidCell(cell) && remoteByCell.TryGetValue(cell, out data);
		}

		private bool SpawnOrUpdatePlant(PlantData data)
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
				DebugConsole.LogWarning($"[PlantGrowthSyncer] Could not find prefab for plant '{data.PlantPrefabTag}'");
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
				DebugConsole.LogWarning($"[PlantGrowthSyncer] Spawned plant '{data.PlantPrefabTag}' but could not resolve Growing at cell {data.Cell}");
				return false;
			}

			ApplyPlantState(spawnedPlant, data);

			DebugConsole.Log(receptacle != null
				? $"[PlantGrowthSyncer] Spawned planted crop '{data.PlantPrefabTag}' at cell {data.Cell} for receptacle {data.ReceptacleNetId}"
				: $"[PlantGrowthSyncer] Spawned wild plant '{data.PlantPrefabTag}' at cell {data.Cell}");

			return true;
		}

		private bool RemovePlant(PlantData data)
		{
			using var _ = Profiler.Scope();

			if (TryFindLocalPlant(data, out var growing) && growing != null && growing.gameObject != null)
			{
				Util.KDestroyGameObject(growing.gameObject);
				DebugConsole.Log($"[PlantGrowthSyncer] Removed plant '{data.PlantPrefabTag}' at cell {data.Cell}");
				return true;
			}

			var receptacle = ResolveReceptacle(data);
			if (receptacle?.Occupant != null)
			{
				Util.KDestroyGameObject(receptacle.Occupant);
				DebugConsole.Log($"[PlantGrowthSyncer] Removed receptacle occupant for plant '{data.PlantPrefabTag}' at cell {data.Cell}");
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

			lock (PlantTracker.AllPlants)
			{
				foreach (var trackedPlant in PlantTracker.AllPlants)
				{
					if (trackedPlant == null || trackedPlant.gameObject == null)
						continue;

					int cell = Grid.PosToCell(trackedPlant.gameObject);
					if (cell != data.Cell)
						continue;

					if (!trackedPlant.TryGetComponent<KPrefabID>(out var kpid) || kpid == null)
						continue;

					if (!string.Equals(kpid.PrefabTag.Name, data.PlantPrefabTag))
						continue;

					growing = trackedPlant;
					return true;
				}
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

			var identity = go.AddOrGet<NetworkIdentity>();
			if (identity.NetId == 0)
			{
				identity.RegisterIdentity();
			}

			if (targetNetId != 0 && identity.NetId != targetNetId)
			{
				identity.OverrideNetId(targetNetId);
			}

			return identity.NetId;
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
				DebugConsole.LogError($"[PlantGrowthSyncer] Error applying plant state at cell {data.Cell}: {ex.Message}");
			}
		}
	}
}
