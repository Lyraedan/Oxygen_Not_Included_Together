using System.Collections.Generic;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.Trackers;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Components
{
	public partial class PlantGrowthSyncer
	{
		private static ulong _lastAppliedSnapshotRevision;

		private sealed class PlantSnapshotIndex
		{
			private readonly Dictionary<int, PlantData> _byPlantId = new();
			private readonly Dictionary<int, PlantData> _byReceptacleId = new();
			private readonly Dictionary<int, PlantData> _byCell = new();
			private readonly HashSet<int> _matchedPlantIds = new();

			internal PlantSnapshotIndex(IEnumerable<PlantData> plants)
			{
				foreach (PlantData plant in plants)
				{
					_byPlantId[plant.PlantNetId] = plant;
					if (plant.ReceptacleNetId != 0)
						_byReceptacleId[plant.ReceptacleNetId] = plant;
					if (Grid.IsValidCell(plant.Cell))
						_byCell[plant.Cell] = plant;
				}
			}

			internal bool TryFind(Growing growing, out PlantData data)
			{
				data = default;
				int plantNetId = GetExistingIdentityId(growing.gameObject);
				if (plantNetId != 0)
					return _byPlantId.TryGetValue(plantNetId, out data);
				if (TryGetReceptacle(growing, out var receptacle))
				{
					int receptacleNetId = GetExistingIdentityId(receptacle.gameObject);
					if (receptacleNetId != 0 && _byReceptacleId.TryGetValue(receptacleNetId, out data))
						return true;
				}
				int cell = Grid.PosToCell(growing.gameObject);
				return Grid.IsValidCell(cell) && _byCell.TryGetValue(cell, out data);
			}

			internal void MarkMatched(PlantData data) => _matchedPlantIds.Add(data.PlantNetId);

			internal bool IsMatched(PlantData data) => _matchedPlantIds.Contains(data.PlantNetId);
		}

		public void OnPlantStateReceived(PlantGrowthStatePacket packet)
		{
			using var _ = Profiler.Scope();
			if (MultiplayerSession.IsHost || Grid.WidthInCells == 0
			    || !ShouldApplySnapshotRevision(_lastAppliedSnapshotRevision, packet.SnapshotRevision))
				return;
			try
			{
				IsApplyingState = true;
				ApplyPlantSnapshot(packet);
				_lastAppliedSnapshotRevision = packet.SnapshotRevision;
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

		private void ApplyPlantSnapshot(PlantGrowthStatePacket packet)
		{
			var index = new PlantSnapshotIndex(packet.Plants);
			var absent = new List<Growing>();
			lock (PlantTracker.AllPlants)
			{
				foreach (Growing growing in PlantTracker.AllPlants)
				{
					if (growing != null && index.TryFind(growing, out PlantData data)
					    && TryApplyExistingPlant(growing, data))
					{
						index.MarkMatched(data);
						continue;
					}
					if (growing != null)
						absent.Add(growing);
				}
			}
			RemovePlantsAbsentAtCut(absent, packet.SnapshotRevision);
			foreach (PlantData data in packet.Plants)
				if (!index.IsMatched(data))
					SpawnOrUpdatePlant(data);
		}

		private static void RemovePlantsAbsentAtCut(IEnumerable<Growing> plants, ulong snapshotRevision)
		{
			foreach (Growing growing in plants)
			{
				if (growing == null || growing.gameObject == null)
					continue;
				ulong lifecycle = GetExistingLifecycleRevision(growing.gameObject);
				if (!ShouldRemoveAbsentPlant(lifecycle, snapshotRevision))
					continue;
				DebugConsole.Log($"[PlantGrowthSyncer] Removing phantom plant at {Grid.PosToCell(growing)}");
				Util.KDestroyGameObject(growing.gameObject);
			}
		}

		private bool SpawnOrUpdatePlant(PlantData data)
		{
			using var _ = Profiler.Scope();
			if (!Grid.IsValidCell(data.Cell) || !PlantData.IsValid(data))
				return false;
			if (!CanApplyIncomingPlant(data))
				return true;
			if (TryFindLocalPlant(data, out Growing existingPlant))
				return HasNewerDifferentLifecycle(existingPlant, data)
					? true
					: TryApplyExistingPlant(existingPlant, data);

			SingleEntityReceptacle receptacle = ResolveReceptacle(data);
			if (receptacle?.Occupant != null
			    && receptacle.Occupant.TryGetComponent(out Growing occupantPlant))
			{
				if (NetworkIdentityRegistry.IsAvailableBindingCandidate(
					    receptacle.Occupant))
				{
					if (HasNewerDifferentLifecycle(occupantPlant, data))
						return true;
					if (IsSamePlant(occupantPlant, data))
						return TryApplyExistingPlant(occupantPlant, data);
				}
				Util.KDestroyGameObject(receptacle.Occupant);
			}
			return TryMaterializePlant(data, receptacle);
		}

		private static bool TryApplyExistingPlant(Growing growing, PlantData data)
		{
			if (growing == null || growing.gameObject == null || !CanApplyIncomingPlant(data))
				return false;
			if (HasNewerDifferentLifecycle(growing, data)
			    || !TryBindPlantLifecycle(growing.gameObject, data))
				return false;
			ApplyPlantState(growing, data);
			return true;
		}

		private static bool TryBindPlantLifecycle(GameObject gameObject, PlantData data)
		{
			if (!NetworkIdentityRegistry.TryBindAuthoritativeLifecycle(
				    gameObject, data.PlantNetId, data.LifecycleRevision))
				return false;
			if (gameObject.TryGetComponent(out NetworkIdentity identity))
				identity.LifecycleRevision = data.LifecycleRevision;
			return true;
		}

		private bool TryMaterializePlant(PlantData data, SingleEntityReceptacle receptacle)
		{
			GameObject prefab = Assets.GetPrefab(new Tag(data.PlantPrefabTag));
			if (prefab == null)
			{
				DebugConsole.LogWarning($"[PlantGrowthSyncer] Could not find prefab for plant '{data.PlantPrefabTag}'");
				return false;
			}
			Vector3 position = Grid.CellToPosCBC(data.Cell, Grid.SceneLayer.BuildingFront);
			GameObject plantObject = Util.KInstantiate(prefab, position);
			if (plantObject == null)
				return false;
			plantObject.SetActive(true);
			PlaceInReceptacle(plantObject, receptacle);
			Growing growing = plantObject.GetComponent<Growing>();
			if (growing == null || !TryBindPlantLifecycle(plantObject, data))
			{
				Util.KDestroyGameObject(plantObject);
				return false;
			}
			ApplyPlantState(growing, data);
			LogMaterializedPlant(data, receptacle != null);
			return true;
		}

		private static void PlaceInReceptacle(GameObject plantObject, SingleEntityReceptacle receptacle)
		{
			if (receptacle is PlantablePlot plot)
			{
				plot.ReplacePlant(plantObject, true);
				if (plantObject.TryGetComponent(out ReceptacleMonitor monitor))
					monitor.SetReceptacle(plot);
				return;
			}
			if (receptacle == null)
				return;
			receptacle.CancelActiveRequest();
			receptacle.ForceDeposit(plantObject);
		}

		private static void LogMaterializedPlant(PlantData data, bool hasReceptacle)
		{
			DebugConsole.Log(hasReceptacle
				? $"[PlantGrowthSyncer] Spawned planted crop '{data.PlantPrefabTag}' at cell {data.Cell} for receptacle {data.ReceptacleNetId}"
				: $"[PlantGrowthSyncer] Spawned wild plant '{data.PlantPrefabTag}' at cell {data.Cell}");
		}

		private bool RemovePlant(PlantData data)
		{
			using var _ = Profiler.Scope();
			if (!PlantData.IsValid(data) || !CanApplyIncomingPlant(data))
				return true;
			ulong current = NetworkIdentityRegistry.GetLastLifecycleRevision(data.PlantNetId);
			if (current < data.LifecycleRevision
			    && !NetworkIdentityRegistry.TryAcceptLifecycleRevision(
				    data.PlantNetId, data.LifecycleRevision, tombstone: false))
				return false;
			if (TryFindLocalPlant(data, out Growing growing)
			    && !HasNewerDifferentLifecycle(growing, data))
			{
				Util.KDestroyGameObject(growing.gameObject);
				DebugConsole.Log($"[PlantGrowthSyncer] Removed plant '{data.PlantPrefabTag}' at cell {data.Cell}");
				return true;
			}
			return RemoveReceptacleOccupant(data);
		}

		private static bool RemoveReceptacleOccupant(PlantData data)
		{
			SingleEntityReceptacle receptacle = ResolveReceptacle(data);
			if (receptacle?.Occupant == null)
				return true;
			if (receptacle.Occupant.TryGetComponent(out Growing growing)
			    && HasNewerDifferentLifecycle(growing, data))
				return true;
			Util.KDestroyGameObject(receptacle.Occupant);
			DebugConsole.Log($"[PlantGrowthSyncer] Removed receptacle occupant for plant '{data.PlantPrefabTag}' at cell {data.Cell}");
			return true;
		}

		private static bool TryFindLocalPlant(PlantData data, out Growing growing)
		{
			using var _ = Profiler.Scope();
			if (NetworkIdentityRegistry.TryGetComponent(data.PlantNetId, out growing) && growing != null)
				return true;
			if (data.ReceptacleNetId != 0
			    && NetworkIdentityRegistry.TryGet(data.ReceptacleNetId, out var identity)
			    && identity.gameObject.TryGetComponent(out SingleEntityReceptacle receptacle)
			    && receptacle.Occupant != null
			    && NetworkIdentityRegistry.IsAvailableBindingCandidate(receptacle.Occupant)
			    && receptacle.Occupant.TryGetComponent(out growing))
				return true;
			return TryFindTrackedPlant(data, out growing);
		}

		private static bool TryFindTrackedPlant(PlantData data, out Growing growing)
		{
			lock (PlantTracker.AllPlants)
			{
				foreach (Growing tracked in PlantTracker.AllPlants)
				{
					if (tracked == null || tracked.gameObject == null
					    || !NetworkIdentityRegistry.IsAvailableBindingCandidate(tracked.gameObject)
					    || Grid.PosToCell(tracked.gameObject) != data.Cell)
						continue;
					if (!tracked.TryGetComponent(out KPrefabID prefabId)
					    || prefabId.PrefabTag.Name != data.PlantPrefabTag)
						continue;
					growing = tracked;
					return true;
				}
			}
			growing = null;
			return false;
		}

		private static SingleEntityReceptacle ResolveReceptacle(PlantData data)
		{
			using var _ = Profiler.Scope();
			if (data.ReceptacleNetId != 0
			    && NetworkIdentityRegistry.TryGet(data.ReceptacleNetId, out var identity)
			    && identity.gameObject.TryGetComponent(out SingleEntityReceptacle byId))
				return byId;
			if (!Grid.IsValidCell(data.Cell))
				return null;
			ObjectLayer[] layers =
			{
				ObjectLayer.Building,
				ObjectLayer.FoundationTile,
				ObjectLayer.Plants,
				ObjectLayer.AttachableBuilding,
			};
			foreach (ObjectLayer layer in layers)
			{
				GameObject candidate = Grid.Objects[data.Cell, (int)layer];
				if (candidate != null && candidate.TryGetComponent(out SingleEntityReceptacle receptacle))
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
			if (growing.TryGetComponent(out ReceptacleMonitor monitor))
			{
				receptacle = monitor.GetReceptacle();
				if (receptacle != null)
					return true;
			}
			receptacle = growing.GetComponentInParent<SingleEntityReceptacle>();
			return receptacle != null;
		}

		private static int EnsureIdentity(GameObject gameObject)
		{
			using var _ = Profiler.Scope();
			if (gameObject == null)
				return 0;
			NetworkIdentity identity = gameObject.AddOrGet<NetworkIdentity>();
			if (identity.NetId == 0)
				identity.RegisterIdentity();
			return identity.NetId;
		}

		private static int GetExistingIdentityId(GameObject gameObject)
		{
			return gameObject != null
			       && gameObject.TryGetComponent(out NetworkIdentity identity)
				? identity.NetId
				: 0;
		}

		private static ulong GetExistingLifecycleRevision(GameObject gameObject)
		{
			int netId = GetExistingIdentityId(gameObject);
			return NetworkIdentityRegistry.GetLastLifecycleRevision(netId);
		}

		private static bool IsSamePlant(Growing growing, PlantData data)
		{
			if (GetExistingIdentityId(growing.gameObject) == data.PlantNetId)
				return true;
			return growing.TryGetComponent(out KPrefabID prefabId)
			       && prefabId.PrefabTag.Name == data.PlantPrefabTag;
		}

		private static bool HasNewerDifferentLifecycle(Growing growing, PlantData data)
		{
			int localNetId = GetExistingIdentityId(growing?.gameObject);
			if (localNetId == 0 || localNetId == data.PlantNetId)
				return false;
			return NetworkIdentityRegistry.GetLastLifecycleRevision(localNetId)
			       >= data.LifecycleRevision;
		}

		private static bool CanApplyIncomingPlant(PlantData data)
		{
			ulong current = NetworkIdentityRegistry.GetLastLifecycleRevision(data.PlantNetId);
			return ShouldApplyPlantRevision(current,
				NetworkIdentityRegistry.IsLifecycleTombstoned(data.PlantNetId),
				data.LifecycleRevision);
		}

		internal static bool ShouldApplyPlantRevision(
			ulong current, bool tombstoned, ulong incoming)
		{
			return incoming != 0
			       && (incoming > current || incoming == current && !tombstoned);
		}

		internal static bool ShouldApplySnapshotRevision(ulong current, ulong incoming)
			=> incoming != 0 && incoming > current;

		internal static bool ShouldRemoveAbsentPlant(ulong localLifecycle, ulong snapshotRevision)
			=> localLifecycle < snapshotRevision;

		internal static void ResetSessionState()
		{
			_lastAppliedSnapshotRevision = 0;
			if (Instance == null)
				return;
			Instance._initialized = false;
			Instance._lastSyncTime = 0f;
			Instance._initializationTime = 0f;
		}

		private static void ApplyPlantState(Growing growing, PlantData data)
		{
			using var _ = Profiler.Scope();
			if (growing == null || growing.gameObject == null)
				return;
			try
			{
				if (Mathf.Abs(growing.PercentGrown() - data.Maturity) > 0.001f)
					growing.OverrideMaturityLevel(data.Maturity);
				if (!data.IsWild && TryGetReceptacle(growing, out var receptacle)
				    && receptacle is PlantablePlot plot
				    && growing.TryGetComponent(out ReceptacleMonitor monitor))
					monitor.SetReceptacle(plot);
				ApplyWiltState(growing, data.IsWilting);
				if (growing.TryGetComponent(out KBatchedAnimController controller))
				{
					controller.SetVisiblity(true);
					controller.forceRebuild = true;
				}
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[PlantGrowthSyncer] Error applying plant state at cell {data.Cell}: {ex.Message}");
			}
		}

		private static void ApplyWiltState(Growing growing, bool shouldWilt)
		{
			if (!growing.TryGetComponent(out WiltCondition condition))
				return;
			if (shouldWilt && !condition.IsWilting())
				condition.DoWilt();
			else if (!shouldWilt && condition.IsWilting())
				condition.DoRecover();
		}
	}
}
