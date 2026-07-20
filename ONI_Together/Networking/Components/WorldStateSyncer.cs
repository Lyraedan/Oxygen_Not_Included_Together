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

		// Rotating background scan - covers off-screen areas
		private const int BG_SCAN_CHUNK_SIZE = 32;
		private int _bgScanIndex = 0;

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

		public IReadOnlyDictionary<ulong, RectInt> ClientViewports => _clientViewports;

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
				if (!MultiplayerSession.ConnectedPlayers.TryGetValue(kvp.Key, out var player) || player.Connection == null)
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
                if (!MultiplayerSession.ConnectedPlayers.TryGetValue(kvp.Key, out var player) || player.Connection == null)
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

			if (!MultiplayerSession.InActiveSession || !MultiplayerSession.IsHost)
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

				if (Time.unscaledTime - _lastGasSyncTime > _effectiveGasInterval)
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
			int cellsScanned = 0;

			if (Grid.WidthInCells == 0 || Grid.HeightInCells == 0) return;

			// Initialize Shadow Grid if needed
			if (_shadowElements == null)
			{
				_shadowElements = new ushort[Grid.CellCount];
				_shadowMass = new float[Grid.CellCount];

				// First run: Copy current state to avoid sending entire map!
				for (int i = 0; i < Grid.CellCount; i++)
				{
					_shadowElements[i] = Grid.ElementIdx[i];
					_shadowMass[i] = Grid.Mass[i];
				}
				return; // Wait for next tick to sync *changes*
			}

			// Add local player viewport
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
				ScanArea(x1, y1, x2, y2);
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
				ScanArea(x1, y1, x2, y2);
			}

			// Scan pinned areas
			foreach (var rect in _pinnedAreas)
			{
				int px1 = Mathf.Max(0, rect.xMin);
				int py1 = Mathf.Max(0, rect.yMin);
				int px2 = Mathf.Min(Grid.WidthInCells, rect.xMax);
				int py2 = Mathf.Min(Grid.HeightInCells, rect.yMax);
				cellsScanned += (px2 - px1) * (py2 - py1);
				ScanArea(px1, py1, px2, py2);
			}

			// Rotating background scan - covers entire map over ~30 seconds
			if (_shadowElements != null)
			{
				int totalCells = Grid.CellCount;
				int totalChunks = Mathf.CeilToInt((float)totalCells / (BG_SCAN_CHUNK_SIZE * BG_SCAN_CHUNK_SIZE));

				int chunkX = (_bgScanIndex % Mathf.CeilToInt((float)Grid.WidthInCells / BG_SCAN_CHUNK_SIZE)) * BG_SCAN_CHUNK_SIZE;
				int chunkY = (_bgScanIndex / Mathf.CeilToInt((float)Grid.WidthInCells / BG_SCAN_CHUNK_SIZE)) * BG_SCAN_CHUNK_SIZE;

				int bgX2 = Mathf.Min(chunkX + BG_SCAN_CHUNK_SIZE, Grid.WidthInCells);
				int bgY2 = Mathf.Min(chunkY + BG_SCAN_CHUNK_SIZE, Grid.HeightInCells);

				cellsScanned += (bgX2 - chunkX) * (bgY2 - chunkY);
				ScanArea(chunkX, chunkY, bgX2, bgY2);

				_bgScanIndex = (_bgScanIndex + 1) % Mathf.Max(1, totalChunks);
			}

			// Flush the batcher
			int packetSize = ONI_Together.Misc.World.WorldUpdateBatcher.Flush();

			sw.Stop();
			SyncStats.RecordSync(SyncStats.Gas, cellsScanned, packetSize, sw.ElapsedMilliseconds);
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

		private void ScanArea(int x1, int y1, int x2, int y2)
		{
			using var _ = Profiler.Scope();

			for (int y = y1; y < y2; y++)
			{
				for (int x = x1; x < x2; x++)
				{
					int cell = y * Grid.WidthInCells + x;
					if (!Grid.IsValidCell(cell)) continue;

					ushort currentElement = Grid.ElementIdx[cell];
					float currentMass = Grid.Mass[cell];

					// Optimization: Ignore very small mass changes?
					// Gas flow changes mass constantly.
					bool changed = false;

					if (currentElement != _shadowElements[cell]) changed = true;
					else if (Mathf.Abs(currentMass - _shadowMass[cell]) > 0.01f) changed = true; // 10g threshold

					if (changed)
					{
						// Update Shadow
						_shadowElements[cell] = currentElement;
						_shadowMass[cell] = currentMass;

						// Queue for Network
						ONI_Together.Misc.World.WorldUpdateBatcher.Queue(new WorldUpdatePacket.CellUpdate
						{
							Cell = cell,
							ElementIdx = currentElement,
							Mass = currentMass,
							Temperature = Grid.Temperature[cell],
							DiseaseIdx = Grid.DiseaseIdx[cell],
							DiseaseCount = Grid.DiseaseCount[cell]
						});
					}
				}
			}
		}
	}
}
