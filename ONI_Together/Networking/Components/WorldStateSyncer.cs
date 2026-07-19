using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.Trackers;
using ONI_Together.Networking.Transport.Steamworks;
using System.Collections.Generic;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Components
{
	public class WorldStateSyncer : MonoBehaviour
	{
		public static WorldStateSyncer Instance { get; private set; }

		// Staggered sync - each sync runs every 5s but distributed across frames
		private const float STAGGERED_SYNC_INTERVAL = 1f;
		private float _lastSyncTime;
		private int _syncCycleIndex = 0;

		// Gas/Liquid Sync - adaptive based on FPS
		private float _lastGasSyncTime;
		private const float GAS_SYNC_INTERVAL = 1.5f; // Increased from 0.2s
		private float _effectiveGasInterval = GAS_SYNC_INTERVAL;

		// Grace period - skip syncs for first few seconds after world load
		private bool _initialized = false;
		private float _initializationTime;
		private const float INITIAL_DELAY = 5f;

		// Game info update - runs regardless of client count for lobby browser
		private float _lastGameInfoTime;
		private const float GAME_INFO_INTERVAL = 5f;

		private ushort[] _shadowElements;
		private float[] _shadowMass;
		private float[] _shadowTemperature;
		private byte[] _shadowDiseaseIdx;
		private int[] _shadowDiseaseCount;

		// Rotating background scan - covers off-screen areas
		private const int BG_SCAN_CHUNK_SIZE = 32;
		private const float BACKGROUND_SWEEP_TARGET_SECONDS = 30f;
		private int _bgScanIndex = 0;
		private int _bgScanCellOffset;
		private static bool _authoritativeRepairSuppressed;
		private static bool _worldScanPaused;

		// Pinned areas - always synced regardless of viewport
		private static readonly List<RectInt> _pinnedAreas = new List<RectInt>();

		public static void PinArea(int x, int y, int width, int height)
		{
			_pinnedAreas.Add(new RectInt(x, y, width, height));
		}

		public static void UnpinArea(int x, int y, int width, int height)
		{
			_pinnedAreas.RemoveAll(r => r.x == x && r.y == y && r.width == width && r.height == height);
		}

		public static void ClearPinnedAreas()
		{
			_pinnedAreas.Clear();
		}

		/// <summary>
		/// All the connected players viewports as updated by their Player Cursor Packet
		/// </summary>
		private readonly Dictionary<ulong, RectInt> _clientViewports = new Dictionary<ulong, RectInt>();

		private void Awake()
		{
			using var _ = Profiler.Scope();

			Instance = this;
		}

		public void UpdateClientView(ulong userId, int minX, int minY, int maxX, int maxY)
		{
			using var _ = Profiler.Scope();

			// Update or add
			_clientViewports[userId] = new RectInt(minX, minY, maxX - minX, maxY - minY);
		}

		public void GetClientsViewingCell(int cell, HashSet<ulong> recipients, int margin = 2)
		{
			using var _ = Profiler.Scope();

			recipients.Clear();
			if (!Grid.IsValidCell(cell))
				return;

			Grid.CellToXY(cell, out int x, out int y);
			foreach (var kvp in _clientViewports)
			{
				if (!MultiplayerSession.ConnectedPlayers.TryGetValue(kvp.Key, out var player)
				    || !CanReceiveViewportRuntime(player))
					continue;

				var rect = kvp.Value;
				if (x >= rect.xMin - margin
					&& x < rect.xMax + margin
					&& y >= rect.yMin - margin
					&& y < rect.yMax + margin)
				{
					recipients.Add(kvp.Key);
				}
			}
		}

		internal static bool CanReceiveViewportRuntime(MultiplayerPlayer player)
			=> player?.Connection != null
			   && player.ProtocolVerified
			   && SyncBarrier.IsExactReady(player.readyState);

		public bool IsCellVisibleToAnyClient(int cell, int margin = 2)
		{
			using var _ = Profiler.Scope();

			var recipients = new HashSet<ulong>();
			GetClientsViewingCell(cell, recipients, margin);
			return recipients.Count > 0;
		}

		/// <summary>
		/// Uses the existing _clientViewports list to check if it is visible
		/// </summary>
        public bool IsCellInPlayerViewport(ulong userId, int cell, int margin = 2)
        {
            if (!_clientViewports.TryGetValue(userId, out var rect))
                return false;
            Grid.CellToXY(cell, out int x, out int y);
            return x >= rect.xMin - margin && x < rect.xMax + margin &&
                   y >= rect.yMin - margin && y < rect.yMax + margin;
        }

		/// <summary>
		/// Uses the existing _clientViewports list to check if it is visible
		/// </summary>
        public bool IsCellVisibleToAnyClientViewport(int cell, int margin = 2)
        {
            if (!Grid.IsValidCell(cell)) return false;
            Grid.CellToXY(cell, out int x, out int y);
            foreach (var kvp in _clientViewports)
            {
                if (!MultiplayerSession.ConnectedPlayers.TryGetValue(kvp.Key, out var player)
                    || !CanReceiveViewportRuntime(player))
                    continue;
                var rect = kvp.Value;
                if (x >= rect.xMin - margin && x < rect.xMax + margin &&
                    y >= rect.yMin - margin && y < rect.yMax + margin)
                    return true;
            }
            return false;
        }

        public static bool IsCellInRect(int cell, RectInt rect, int margin = 2)
        {
            Grid.CellToXY(cell, out int x, out int y);
            return x >= rect.xMin - margin && x < rect.xMax + margin &&
                   y >= rect.yMin - margin && y < rect.yMax + margin;
        }

        public static bool TryGetLocalViewport(out RectInt viewport, int margin = 2)
		{
			using var _ = Profiler.Scope();

			viewport = default;
			if (Camera.main == null || Grid.WidthInCells == 0 || Grid.HeightInCells == 0)
				return false;

			Camera cam = Camera.main;
			Vector3 bl = cam.ViewportToWorldPoint(new Vector3(0, 0, 0));
			Vector3 tr = cam.ViewportToWorldPoint(new Vector3(1, 1, 0));
			Grid.PosToXY(bl, out int x1, out int y1);
			Grid.PosToXY(tr, out int x2, out int y2);

			x1 = Mathf.Max(0, x1 - margin);
			y1 = Mathf.Max(0, y1 - margin);
			x2 = Mathf.Min(Grid.WidthInCells, x2 + margin);
			y2 = Mathf.Min(Grid.HeightInCells, y2 + margin);

			viewport = new RectInt(x1, y1, Mathf.Max(0, x2 - x1), Mathf.Max(0, y2 - y1));
			return viewport.width > 0 && viewport.height > 0;
		}

		private void Update()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost)
				return;

			// Update game info even when no clients connected (for lobby browser)
			// This runs every 5 seconds regardless of client count
			if (Time.unscaledTime - _lastGameInfoTime > GAME_INFO_INTERVAL)
			{
				_lastGameInfoTime = Time.unscaledTime;
				SteamLobby.UpdateGameInfo();
			}

			// Skip other syncs if no clients connected
			if (MultiplayerSession.ConnectedPlayers.Count == 0)
				return;

			// Grace period after world load
			if (!_initialized)
			{
				_initializationTime = Time.unscaledTime;
				_initialized = true;
				return;
			}

			if (Time.unscaledTime - _initializationTime < INITIAL_DELAY)
				return;

			try
			{
				// Adaptive gas sync based on FPS and client count
				_effectiveGasInterval = GAS_SYNC_INTERVAL * GetSyncMultiplier();

				if (ShouldRunWorldScan(_worldScanPaused)
				    && Time.unscaledTime - _lastGasSyncTime > _effectiveGasInterval)
				{
					_lastGasSyncTime = Time.unscaledTime;
					SyncGasLiquid();
				}

				// Staggered syncs - one per second (each runs every 4s but distributed)
				// NOTE: Priorities and Disinfect removed - already synced via event-driven patches
				if (Time.unscaledTime - _lastSyncTime > STAGGERED_SYNC_INTERVAL)
				{
					_lastSyncTime = Time.unscaledTime;
					switch (_syncCycleIndex++ % 4)
					{
						case 0: SyncDigging(); break;
						case 1: SyncChores(); break;
						case 2: SyncResearchProgress(); break;
						case 3: SteamLobby.UpdateGameInfo(); break; // Update lobby metadata
					}
				}
			}
			catch (System.Exception)
			{
				// Silently ignore - sync may fail on freshly loaded world
			}
		}

		// --- Digging Logic ---

			private void SyncDigging()
		{
			using var _ = Profiler.Scope();

			var sw = System.Diagnostics.Stopwatch.StartNew();
			var digPacket = new DiggingStatePacket();

			try
			{
				foreach (var diggable in global::Components.Diggables.Items)
				{
					if (diggable == null) continue;
					int cell = Grid.PosToCell(diggable);
					if (Grid.IsValidCell(cell))
					{
						digPacket.DigCells.Add(cell);
					}
				}

				PacketSender.SendToAllClients(digPacket, PacketSendMode.Unreliable);

				sw.Stop();
				SyncStats.RecordSync(SyncStats.Digging, digPacket.DigCells.Count, digPacket.DigCells.Count * 4, sw.ElapsedMilliseconds);
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[WorldStateSyncer] Error in SyncDigging: {ex.Message}");
			}
		}

		public void OnDiggingStateReceived(DiggingStatePacket packet)
		{
			using var _ = Profiler.Scope();

			// Reconcile
			// 1. Get all local diggables
			// 2. Remove extra
			// 3. Add missing

			try
			{
				var localDigs = new HashSet<int>();
				var toRemove = new List<Diggable>();

				foreach (var diggable in global::Components.Diggables.Items)
				{
					int cell = Grid.PosToCell(diggable);
					localDigs.Add(cell);
					if (!packet.DigCells.Contains(cell))
					{
						toRemove.Add(diggable);
					}
				}

				// Remove Phantoms
				foreach (var d in toRemove)
				{
					//DebugConsole.Log($"[WorldStateSyncer] Removing phantom dig at {Grid.PosToCell(d)}");
					d.gameObject.DeleteObject();
				}

				// Add Missing
				foreach (var cell in packet.DigCells)
				{
					if (!localDigs.Contains(cell))
					{
						//DebugConsole.Log($"[WorldStateSyncer] Adding missing dig at {cell}");
						// Use DigTool logic without sending a packet back!
						// We can manually instantiate the DigPlacer.
						if (Grid.IsValidCell(cell) && Grid.Solid[cell])
						{
							// DigTool.PlaceDig might trigger patches.
							// We should instantiate the prefab directly to avoid triggering client->host packets.
							GameObject prefab = Assets.GetPrefab("DigPlacer");
							if (prefab != null)
							{
								Vector3 pos = Grid.CellToPosCBC(cell, Grid.SceneLayer.Move);
								GameObject go = Util.KInstantiate(prefab, pos);
								go.SetActive(true);
							}
						}
					}
				}
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[WorldStateSyncer] Error in OnDiggingStateReceived: {ex.Message}");
			}
		}

		// --- Chore Logic (Mopping) ---

		private void SyncChores()
		{
			using var _ = Profiler.Scope();

			var sw = System.Diagnostics.Stopwatch.StartNew();
			var chorePacket = new ChoreStatePacket();

			try
			{
				// Use our tracked mop placers
				lock (MopTracker.MopPlacers)
				{
					foreach (var go in MopTracker.MopPlacers)
					{
						if (go == null) continue;
						int cell = Grid.PosToCell(go);
						chorePacket.Chores.Add(new ChoreData { Cell = cell, Type = SyncedChoreType.Mop });
					}
				}

				PacketSender.SendToAllClients(chorePacket, PacketSendMode.Unreliable);

				sw.Stop();
				SyncStats.RecordSync(SyncStats.Chores, chorePacket.Chores.Count, chorePacket.Chores.Count * 5, sw.ElapsedMilliseconds);
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[WorldStateSyncer] Error in SyncChores: {ex}");
			}
		}

		public void OnChoreStateReceived(ChoreStatePacket packet)
		{
			using var _ = Profiler.Scope();

			try
			{
				// Reconcile Mops
				var localMops = new HashSet<int>();
				var toRemove = new List<GameObject>();

				lock (MopTracker.MopPlacers)
				{
					// Identification Phase
					foreach (var go in MopTracker.MopPlacers)
					{
						if (go == null) continue;
						int cell = Grid.PosToCell(go);
						localMops.Add(cell);

						// Check if phantom
						bool existsRemote = false;
						foreach (var c in packet.Chores)
						{
							if (c.Cell == cell && c.Type == SyncedChoreType.Mop)
							{
								existsRemote = true;
								break;
							}
						}

						if (!existsRemote)
						{
							toRemove.Add(go);
						}
					}
				}

				// Removal Phase
				foreach (var go in toRemove)
				{
					go.DeleteObject();
					// MopTracker will update via OnCleanUp patch automatically
				}

				// Addition Phase
				foreach (var c in packet.Chores)
				{
					if (c.Type == SyncedChoreType.Mop && !localMops.Contains(c.Cell))
					{
						// Spawn Mop Placer
						if (Grid.IsValidCell(c.Cell))
						{
							var mopPrefab = Assets.GetPrefab(new Tag("MopPlacer"));
							if (mopPrefab != null)
							{
								GameObject placer = Util.KInstantiate(mopPrefab);
								Vector3 position = Grid.CellToPosCBC(c.Cell, MopTool.Instance.visualizerLayer);
								position.z -= 0.15f;
								placer.transform.SetPosition(position);
								placer.SetActive(true);

								// Set standard priority if possible (default 5)
								var prioritizable = placer.GetComponent<Prioritizable>();
								if (prioritizable != null && ToolMenu.Instance != null)
									prioritizable.SetMasterPriority(ToolMenu.Instance.PriorityScreen.GetLastSelectedPriority());
							}
						}
					}
				}
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[WorldStateSyncer] Error in OnChoreStateReceived: {ex.Message}");
			}
		}

		// --- Research Logic ---
		private void SyncResearch()
		{
			using var _ = Profiler.Scope();

			if (Db.Get().Techs == null || Research.Instance == null) return;

			try
			{
				var packet = new ResearchStatePacket();

				// Include the current active research
				var activeResearch = Research.Instance.GetActiveResearch();
				packet.ActiveTechId = activeResearch?.tech?.Id ?? string.Empty;

				// Include the research queue
				try
				{
					var queueField = HarmonyLib.AccessTools.Field(typeof(Research), "queuedTech");
					if (queueField != null)
					{
						var queue = queueField.GetValue(Research.Instance) as System.Collections.IList;
						if (queue != null)
						{
							foreach (var item in queue)
							{
								var techInstance = item as TechInstance;
								if (techInstance?.tech != null)
								{
									packet.QueuedTechIds.Add(techInstance.tech.Id);
								}
							}
						}
					}
				}
				catch { }

				if (Db.Get().Techs != null)
				{
					foreach (var tech in Db.Get().Techs.resources)
					{
						var techInst = Research.Instance.Get(tech);
						if (techInst != null && techInst.IsComplete())
						{
							packet.UnlockedTechIds.Add(tech.Id);
						}
					}
				}

				PacketSender.SendToAllClients(packet, PacketSendMode.Unreliable);
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[WorldStateSyncer] Error in SyncResearch: {ex.Message}");
			}
		}

		// --- Research Progress Logic ---
		private void SyncResearchProgress()
		{
			using var _ = Profiler.Scope();

			if (Research.Instance == null) return;

			var sw = System.Diagnostics.Stopwatch.StartNew();
			try
			{
				var activeResearch = Research.Instance.GetActiveResearch();
				if (activeResearch == null || activeResearch.tech == null) return;

				var techInstance = activeResearch;
				var tech = techInstance.tech;

				// Calculate total progress percentage
				float totalCost = 0f;
				float totalProgress = 0f;

				foreach (var researchType in tech.costsByResearchTypeID.Keys)
				{
					float cost = tech.costsByResearchTypeID[researchType];
					float points = techInstance.progressInventory.PointsByTypeID.ContainsKey(researchType)
						? techInstance.progressInventory.PointsByTypeID[researchType]
						: 0f;

					totalCost += cost;
					totalProgress += Mathf.Min(points, cost);
				}

				float progressPercent = totalCost > 0 ? totalProgress / totalCost : 0f;

				var packet = new ResearchProgressPacket
				{
					TechId = tech.Id,
					Progress = progressPercent
				};

				PacketSender.SendToAllClients(packet, PacketSendMode.Unreliable);

				sw.Stop();
				SyncStats.RecordSync(SyncStats.Research, 1, 20, sw.ElapsedMilliseconds);
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[WorldStateSyncer] Error in SyncResearchProgress: {ex.Message}");
			}
		}

		// --- Priorities Logic (NOT USED - synced via event-driven patches) ---
		private void SyncPriorities()
		{
			using var _ = Profiler.Scope();

			try
			{
				var packet = new PrioritizeStatePacket();

				foreach (var identity in NetworkIdentityRegistry.AllIdentities)
				{
					if (identity == null) continue;

					var prioritizable = identity.GetComponent<Prioritizable>();
					if (prioritizable != null && prioritizable.IsPrioritizable())
					{
						var output = prioritizable.GetMasterPriority();

						packet.Priorities.Add(new PrioritizeStatePacket.PriorityData
						{
							NetId = identity.NetId,
							PriorityClass = (int)output.priority_class,
							PriorityValue = output.priority_value
						});
					}
				}

				if (packet.Priorities.Count > 0)
					PacketSender.SendToAllClients(packet, PacketSendMode.Unreliable);
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[WorldStateSyncer] Error in SyncPriorities: {ex.Message}");
			}
		}

	private System.Reflection.FieldInfo _disinfectChoreField;

	// --- Disinfect Logic (NOT USED - synced via event-driven patches) ---
		private void SyncDisinfectImpl()
		{
			using var _ = Profiler.Scope();

			try
			{
				// Use our tracker
				lock (DisinfectTracker.Disinfectables)
				{
					if (DisinfectTracker.Disinfectables.Count == 0) return;

					if (_disinfectChoreField == null)
					{
						_disinfectChoreField = typeof(Disinfectable).GetField("chore", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
					}

					var packet = new DisinfectStatePacket();
					foreach (var disinfectable in DisinfectTracker.Disinfectables)
					{
						if (disinfectable == null) continue;

						object chore = _disinfectChoreField?.GetValue(disinfectable);
						if (chore != null)
						{
							int cell = Grid.PosToCell(disinfectable);
							packet.DisinfectCells.Add(cell);
						}
					}

					if (packet.DisinfectCells.Count > 0)
						PacketSender.SendToAllClients(packet, PacketSendMode.Unreliable);
				}
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[WorldStateSyncer] Error in SyncDisinfectImpl: {ex.Message}");
			}
		}

		public void OnDisinfectStateReceived(DisinfectStatePacket packet)
		{
			using var _ = Profiler.Scope();

			try
			{
				lock (DisinfectTracker.Disinfectables)
				{
					if (DisinfectTracker.Disinfectables.Count == 0) return;

					if (_disinfectChoreField == null)
					{
						_disinfectChoreField = typeof(Disinfectable).GetField("chore", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
					}

					foreach (var disinfectable in DisinfectTracker.Disinfectables)
					{
						if (disinfectable == null) continue;
						int cell = Grid.PosToCell(disinfectable);

						object chore = _disinfectChoreField?.GetValue(disinfectable);
						bool isMarked = chore != null;

						if (packet.DisinfectCells.Contains(cell))
						{
							if (!isMarked)
							{
								disinfectable.MarkForDisinfect();
							}
						}
						else
						{
							if (isMarked)
							{
								disinfectable.Trigger((int)GameHashes.Cancel, null);
							}
						}
					}
				}
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[WorldStateSyncer] Error in OnDisinfectStateReceived: {ex.Message}");
			}
		}
		// --- Gas and Liquid Logic ---
		private void SyncGasLiquid()
		{
			using var _ = Profiler.Scope();
			var sw = System.Diagnostics.Stopwatch.StartNew();
			if (Grid.WidthInCells == 0 || Grid.HeightInCells == 0
			    || !EnsureShadowGrid())
				return;

			int cellsScanned = ScanVisibleAreas();
			cellsScanned += ScanBackgroundSweep();
			int packetSize = ONI_Together.Misc.World.WorldUpdateBatcher.Flush();
			sw.Stop();
			SyncStats.RecordSync(
				SyncStats.Gas, cellsScanned, packetSize, sw.ElapsedMilliseconds);
		}

		private bool EnsureShadowGrid()
		{
			if (_shadowElements != null && _shadowElements.Length == Grid.CellCount)
				return true;
			_shadowElements = new ushort[Grid.CellCount];
			_shadowMass = new float[Grid.CellCount];
			_shadowTemperature = new float[Grid.CellCount];
			_shadowDiseaseIdx = new byte[Grid.CellCount];
			_shadowDiseaseCount = new int[Grid.CellCount];
			_bgScanIndex = 0;
			_bgScanCellOffset = 0;
			for (int cell = 0; cell < Grid.CellCount; cell++)
				UpdateShadow(cell, CaptureCell(cell));
			return false;
		}

		private int ScanVisibleAreas()
		{
			int cellsScanned = 0;
			if (CursorManager.Instance != null && Camera.main != null)
			{
				Camera cam = Camera.main;
				Vector3 bl = cam.ViewportToWorldPoint(new Vector3(0, 0, 0));
				Vector3 tr = cam.ViewportToWorldPoint(new Vector3(1, 1, 0));
				Grid.PosToXY(bl, out int x1, out int y1);
				Grid.PosToXY(tr, out int x2, out int y2);

				// Add margin
				int margin = 2;
				x1 = Mathf.Max(0, x1 - margin);
				y1 = Mathf.Max(0, y1 - margin);
				x2 = Mathf.Min(Grid.WidthInCells, x2 + margin);
				y2 = Mathf.Min(Grid.HeightInCells, y2 + margin);

				cellsScanned += (x2 - x1) * (y2 - y1);
				ScanArea(new RectInt(x1, y1, x2 - x1, y2 - y1));
			}

			// Scan Client Viewports
			foreach (var kvp in _clientViewports)
			{
				var rect = kvp.Value;
				int x1 = Mathf.Max(0, rect.xMin - 2);
				int y1 = Mathf.Max(0, rect.yMin - 2);
				int x2 = Mathf.Min(Grid.WidthInCells, rect.xMax + 2);
				int y2 = Mathf.Min(Grid.HeightInCells, rect.yMax + 2);

				cellsScanned += (x2 - x1) * (y2 - y1);
				ScanArea(new RectInt(x1, y1, x2 - x1, y2 - y1));
			}

			// Scan pinned areas
			foreach (var rect in _pinnedAreas)
			{
				int px1 = Mathf.Max(0, rect.xMin);
				int py1 = Mathf.Max(0, rect.yMin);
				int px2 = Mathf.Min(Grid.WidthInCells, rect.xMax);
				int py2 = Mathf.Min(Grid.HeightInCells, rect.yMax);
				cellsScanned += (px2 - px1) * (py2 - py1);
				ScanArea(new RectInt(px1, py1, px2 - px1, py2 - py1));
			}
			return cellsScanned;
		}

		private int ScanBackgroundSweep()
		{
			int cellsScanned = 0;
			int totalChunks = BackgroundChunkCount(Grid.WidthInCells, Grid.HeightInCells);
			int chunkBudget = BackgroundChunksPerPass(totalChunks, _effectiveGasInterval);
			int requestedCells = chunkBudget * BG_SCAN_CHUNK_SIZE * BG_SCAN_CHUNK_SIZE;
			int cellBudget = ONI_Together.Misc.World.WorldUpdateBatcher
				.RepairProducerCellBudget(requestedCells, Grid.CellCount);
			for (int chunk = 0; chunk < chunkBudget && cellBudget > 0; chunk++)
			{
				RectInt area = BackgroundChunkBounds(
					Grid.WidthInCells, Grid.HeightInCells, _bgScanIndex);
				int chunkCells = area.width * area.height;
				int attemptBudget = Mathf.Min(
					cellBudget, Mathf.Max(0, chunkCells - _bgScanCellOffset));
				int processed = ScanAuthoritativeArea(
					area, _bgScanCellOffset, attemptBudget,
					_authoritativeRepairSuppressed);
				cellsScanned += processed;
				cellBudget -= processed;
				int previousOffset = _bgScanCellOffset;
				AdvanceBackgroundSweepPosition(
					_bgScanIndex, previousOffset, processed, chunkCells, totalChunks,
					out _bgScanIndex, out _bgScanCellOffset);
				if (previousOffset + processed < chunkCells)
					break;
			}
			return cellsScanned;
		}

		internal bool QueueChangedCellsForCheckpoint()
		{
			if (Grid.WidthInCells == 0 || Grid.HeightInCells == 0)
				return false;
			if (!EnsureShadowGrid())
				return true;
			for (int cell = 0; cell < Grid.CellCount; cell++)
			{
				if (!Grid.IsValidCell(cell))
					continue;
				WorldUpdatePacket.CellUpdate current = CaptureCell(cell);
				if (!ShouldQueueCheckpointCell(CaptureShadow(cell), current))
					continue;
				if (!ONI_Together.Misc.World.WorldUpdateBatcher.Queue(current))
					return false;
				UpdateShadow(cell, current);
			}
			return true;
		}

		/// <summary>
		/// Adaptive sync frequency based on FPS and client count.
		/// Returns multiplier: 1.0 (normal) to 6.0 (heavy load).
		/// </summary>
		private float GetSyncMultiplier()
		{
			float multiplier = 1f;

			// FPS factor
			float fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.001f);
			if (fps < 20f) multiplier *= 3f;
			else if (fps < 30f) multiplier *= 2f;
			else if (fps < 45f) multiplier *= 1.5f;

			// Client count factor
			int clients = MultiplayerSession.ConnectedPlayers.Count;
			if (clients > 4) multiplier *= 2f;
			else if (clients > 2) multiplier *= 1.5f;

			return Mathf.Min(multiplier, 6f);
		}

		private void ScanArea(RectInt area, bool authoritative = false)
		{
			using var _ = Profiler.Scope();

			for (int y = area.yMin; y < area.yMax; y++)
			for (int x = area.xMin; x < area.xMax; x++)
				TryScanCell(y * Grid.WidthInCells + x, authoritative);
		}

		private int ScanAuthoritativeArea(
			RectInt area, int cellOffset, int cellBudget, bool repairSuppressed)
		{
			int processed = 0;
			int chunkCells = area.width * area.height;
			while (processed < cellBudget && cellOffset + processed < chunkCells)
			{
				int local = cellOffset + processed;
				int x = area.xMin + local % area.width;
				int y = area.yMin + local / area.width;
				if (!TryScanCell(
					    y * Grid.WidthInCells + x, authoritative: true,
					    repairSuppressed: repairSuppressed))
					break;
				processed++;
			}
			return processed;
		}

		private bool TryScanCell(
			int cell, bool authoritative, bool repairSuppressed = false)
		{
			if (!Grid.IsValidCell(cell))
				return true;
			WorldUpdatePacket.CellUpdate current = CaptureCell(cell);
			WorldUpdatePacket.CellUpdate shadow = CaptureShadow(cell);
			bool changed = CellStateChanged(shadow, current);
			if (!ShouldQueueCell(authoritative, shadow, current)
			    || authoritative && !ShouldQueueAuthoritativeSweepCell(
				    repairSuppressed, shadow, current))
				return true;
			if (!ONI_Together.Misc.World.WorldUpdateBatcher.Queue(
				    current, ShouldUseBackgroundRepair(authoritative, changed)))
				return false;
			UpdateShadow(cell, current);
			return true;
		}

		private static WorldUpdatePacket.CellUpdate CaptureCell(int cell)
			=> new WorldUpdatePacket.CellUpdate
			{
				Cell = cell,
				ElementIdx = Grid.ElementIdx[cell],
				Mass = Grid.Mass[cell],
				Temperature = Grid.Temperature[cell],
				DiseaseIdx = Grid.DiseaseIdx[cell],
				DiseaseCount = Grid.DiseaseCount[cell],
				ReplaceType = SimMessages.ReplaceType.Replace,
			};

		private WorldUpdatePacket.CellUpdate CaptureShadow(int cell)
			=> new WorldUpdatePacket.CellUpdate
			{
				ElementIdx = _shadowElements[cell],
				Mass = _shadowMass[cell],
				Temperature = _shadowTemperature[cell],
				DiseaseIdx = _shadowDiseaseIdx[cell],
				DiseaseCount = _shadowDiseaseCount[cell],
			};

		private void UpdateShadow(int cell, WorldUpdatePacket.CellUpdate current)
		{
			_shadowElements[cell] = current.ElementIdx;
			_shadowMass[cell] = current.Mass;
			_shadowTemperature[cell] = current.Temperature;
			_shadowDiseaseIdx[cell] = current.DiseaseIdx;
			_shadowDiseaseCount[cell] = current.DiseaseCount;
		}

		internal static bool CellStateChanged(
			WorldUpdatePacket.CellUpdate previous,
			WorldUpdatePacket.CellUpdate current)
		{
			return previous.ElementIdx != current.ElementIdx
				|| !previous.Mass.Equals(current.Mass)
				|| !previous.Temperature.Equals(current.Temperature)
				|| previous.DiseaseIdx != current.DiseaseIdx
				|| previous.DiseaseCount != current.DiseaseCount;
		}

		internal static bool ShouldQueueCell(
			bool authoritative,
			WorldUpdatePacket.CellUpdate previous,
			WorldUpdatePacket.CellUpdate current)
		{
			return authoritative || CellStateChanged(previous, current);
		}

		internal static bool ShouldUseBackgroundRepair(bool authoritative, bool changed)
			=> authoritative && !changed;

		internal static bool ShouldQueueAuthoritativeSweepCell(
			bool repairSuppressed,
			WorldUpdatePacket.CellUpdate previous,
			WorldUpdatePacket.CellUpdate current)
			=> !repairSuppressed || CellStateChanged(previous, current);

		internal static bool ShouldQueueCheckpointCell(
			WorldUpdatePacket.CellUpdate previous,
			WorldUpdatePacket.CellUpdate current)
			=> CellStateChanged(previous, current);

		internal static void SetAuthoritativeRepairSuppressed(bool suppressed)
			=> _authoritativeRepairSuppressed = suppressed;

		internal static void SetWorldScanPaused(bool paused)
			=> _worldScanPaused = paused;

		internal static bool ShouldRunWorldScan(bool paused) => !paused;

		internal static bool AuthoritativeRepairSuppressedForTests
			=> _authoritativeRepairSuppressed;

		internal static bool WorldScanPausedForTests => _worldScanPaused;

		internal static void AdvanceBackgroundSweepPosition(
			int chunkIndex,
			int cellOffset,
			int processedCells,
			int chunkCellCount,
			int totalChunks,
			out int nextChunkIndex,
			out int nextCellOffset)
		{
			nextChunkIndex = chunkIndex;
			nextCellOffset = cellOffset;
			if (chunkIndex < 0 || chunkIndex >= totalChunks || cellOffset < 0
			    || cellOffset >= chunkCellCount || processedCells < 0)
				return;
			nextCellOffset = Mathf.Min(chunkCellCount, cellOffset + processedCells);
			if (nextCellOffset < chunkCellCount)
				return;
			nextChunkIndex = (chunkIndex + 1) % totalChunks;
			nextCellOffset = 0;
		}

		internal static int BackgroundChunkCount(int width, int height)
		{
			if (width <= 0 || height <= 0)
				return 0;
			int chunksX = (width + BG_SCAN_CHUNK_SIZE - 1) / BG_SCAN_CHUNK_SIZE;
			int chunksY = (height + BG_SCAN_CHUNK_SIZE - 1) / BG_SCAN_CHUNK_SIZE;
			return chunksX * chunksY;
		}

		internal static int BackgroundChunksPerPass(int totalChunks, float intervalSeconds)
		{
			if (totalChunks <= 0 || intervalSeconds <= 0f
			    || float.IsNaN(intervalSeconds) || float.IsInfinity(intervalSeconds))
				return 0;
			int budget = Mathf.CeilToInt(
				totalChunks * intervalSeconds / BACKGROUND_SWEEP_TARGET_SECONDS);
			return Mathf.Clamp(budget, 1, totalChunks);
		}

		internal static RectInt BackgroundChunkBounds(int width, int height, int chunkIndex)
		{
			int chunkCount = BackgroundChunkCount(width, height);
			if (chunkIndex < 0 || chunkIndex >= chunkCount)
				return default;
			int chunksPerRow = (width + BG_SCAN_CHUNK_SIZE - 1) / BG_SCAN_CHUNK_SIZE;
			int x = chunkIndex % chunksPerRow * BG_SCAN_CHUNK_SIZE;
			int y = chunkIndex / chunksPerRow * BG_SCAN_CHUNK_SIZE;
			return new RectInt(x, y,
				Mathf.Min(BG_SCAN_CHUNK_SIZE, width - x),
				Mathf.Min(BG_SCAN_CHUNK_SIZE, height - y));
		}
	}
}
