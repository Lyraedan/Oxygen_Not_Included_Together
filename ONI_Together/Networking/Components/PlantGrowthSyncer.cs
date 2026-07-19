using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.Trackers;
using Shared.Profiling;
using UnityEngine;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Components
{
	public partial class PlantGrowthSyncer : MonoBehaviour
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
			if (operation == PlantLifecycleOperation.Remove)
			{
				int netId = GetExistingIdentityId(growing?.gameObject);
				if (!ShouldCaptureRemovalRevision(
					    NetworkIdentityRegistry.GetLastLifecycleRevision(netId),
					    NetworkIdentityRegistry.IsLifecycleTombstoned(netId)))
					return;
			}

			if (!TryBuildPlantData(growing, out var data, receptacleOverride))
				return;

			PacketSender.SendToAllClients(new PlantLifecyclePacket
			{
				Operation = operation,
				Plant = data
			});
		}

		internal static bool ShouldCaptureRemovalRevision(ulong revision, bool tombstoned)
			=> revision != 0 && !tombstoned;

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

			int plantNetId = EnsureIdentity(growing.gameObject);
			ulong lifecycleRevision = EnsureLiveLifecycle(plantNetId);
			if (plantNetId == 0 || lifecycleRevision == 0)
				return false;
			int receptacleNetId = ResolveReceptacleNetId(
				growing, receptacleOverride, out bool isWild);

			data = new PlantData
			{
				PlantNetId = plantNetId,
				LifecycleRevision = lifecycleRevision,
				ReceptacleNetId = receptacleNetId,
				Cell = cell,
				PlantPrefabTag = kpid.PrefabTag.Name,
				Maturity = growing.PercentGrown(),
				IsWilting = growing.TryGetComponent(out WiltCondition wilt) && wilt.IsWilting(),
				IsHarvestReady = growing.TryGetComponent(out HarvestDesignatable harvest)
				                 && harvest.CanBeHarvested(),
				IsWild = isWild
			};
			return true;
		}

		private static int ResolveReceptacleNetId(
			Growing growing, SingleEntityReceptacle receptacle, out bool isWild)
		{
			isWild = growing.IsWildPlanted();
			if (receptacle == null)
				TryGetReceptacle(growing, out receptacle);
			if (receptacle == null || receptacle.gameObject == null)
				return 0;
			isWild = false;
			return EnsureIdentity(receptacle.gameObject);
		}

		private static ulong EnsureLiveLifecycle(int netId)
		{
			if (netId == 0)
				return 0;
			ulong revision = NetworkIdentityRegistry.GetLastLifecycleRevision(netId);
			if (revision == 0 || NetworkIdentityRegistry.IsLifecycleTombstoned(netId))
				revision = NetworkIdentityRegistry.BeginLifecycle(netId);
			if (NetworkIdentityRegistry.TryGet(netId, out NetworkIdentity identity))
				identity.LifecycleRevision = revision;
			return revision;
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
			packet.SnapshotRevision = NetworkIdentityRegistry.NextAuthorityRevision();

			PacketSender.SendToAllClients(packet, PacketSendMode.Unreliable);

			sw.Stop();
			SyncStats.RecordSync(SyncStats.Plants, packet.Plants.Count, packet.Plants.Count * 56, sw.ElapsedMilliseconds);
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
	}
}
