using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.Transport.Steamworks;
using Shared.Profiling;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ONI_Together.Networking.Components
{
	// Periodic sync of pipe (gas + liquid) contents from host to clients.
	// Client sim is frozen (per oni-architecture.md), so pipes show stale or
	// empty contents to clients without an explicit host push.
	//
	// Design notes:
	// - Scans the full map each tick, not just the host's viewport — clients
	//   can be looking at pipes the host has scrolled off-screen, and those
	//   still need to sync (invariant #1 still satisfied via per-cell client
	//   viewport check before any send).
	// - Delta-driven on the fast path (1.5 s) for low bandwidth, but a force
	//   refresh tick re-emits all visible pipe contents every FORCE_REFRESH
	//   seconds so dropped unreliable packets self-heal. Mirrors the
	//   BuildingSyncer "full-state, unreliable, periodic" pattern called out
	//   in oni-architecture.md.
	// - Per-packet update cap chosen to keep on-wire size under Steam P2P's
	//   ~1200 byte MTU for unreliable, so fragmentation does not amplify drops.
	// - Solid rails are out of scope for this iteration: SolidConduitFlow
	//   contents reference a host-local pickupable handle that does not
	//   round-trip through serialisation.
	public class ConduitFlowSyncer : MonoBehaviour
	{
		public static ConduitFlowSyncer Instance { get; private set; }

		private const float SYNC_INTERVAL = 1.5f;        // delta cadence — matches WorldStateSyncer.GAS_SYNC_INTERVAL
		private const float FORCE_REFRESH_INTERVAL = 4.5f; // full re-emit cadence; 3x delta is the smallest spacing that does not duplicate the delta tick
		private const float INITIAL_DELAY = 5f;
		// 22 bytes/update (cell:4 + type:1 + element:4 + mass:4 + temp:4 + disease idx:1 + disease count:4)
		// 50 * 22 = 1100 bytes, fits Steam P2P unreliable MTU (~1200 B) without fragmentation.
		private const int MAX_UPDATES_PER_PACKET = 50;
		private const float MASS_THRESHOLD = 0.01f;      // 10 g
		private const float TEMP_THRESHOLD = 0.5f;       // 0.5 K

		// Per-cell rate limit for the event-driven "empty→non-empty" fast path.
		// Matches SYNC_INTERVAL: if a cell already fired immediately within the
		// last interval, the next sweep tick is already scheduled and would
		// re-broadcast the same state anyway. Defers to invariant #2's "min
		// interval floor" requirement.
		private const float IMMEDIATE_RATE_LIMIT = 1.5f;

		private float _lastSyncTime;
		private float _lastForceRefresh;
		private bool _initialized;
		private float _initializationTime;

		// Shadow grids: track last-broadcast state per cell, per conduit type,
		// so delta ticks only send actual changes. Sized to Grid.CellCount on
		// first use. Force-refresh ticks ignore these and emit everything.
		private int[] _shadowGasElement;
		private float[] _shadowGasMass;
		private float[] _shadowGasTemp;
		private int[] _shadowLiquidElement;
		private float[] _shadowLiquidMass;
		private float[] _shadowLiquidTemp;

		// Fast-path state: per-cell time of last immediate emit (regardless of
		// conduit type since gas/liquid pipes never coexist in the same cell),
		// and an accumulator that LateUpdate flushes once per frame to coalesce
		// bursts (e.g. a reservoir dumping into many cells in a single Sim200ms
		// tick) into a single Reliable packet.
		private float[] _lastImmediateEmit;
		private readonly List<ConduitCellUpdate> _pendingImmediate = new List<ConduitCellUpdate>(64);

		// Reusable scratch HashSet so the per-cell visibility check does not
		// allocate. GetClientsViewingCell clears it for us.
		private readonly HashSet<ulong> _recipientScratch = new HashSet<ulong>();

		// Throttled stats — log first N then sample, per coding-guidelines.
		private int _ticksSinceLog;
		private const int LOG_EVERY_N_TICKS = 20;

		private void Awake()
		{
			using var _ = Profiler.Scope();

			Instance = this;
		}

		private void Update()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession || !MultiplayerSession.IsHost)
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

			try
			{
				SyncConduits();
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[ConduitFlowSyncer] SyncConduits failed: {ex}");
			}
		}

		private void SyncConduits()
		{
			using var _ = Profiler.Scope();

			if (Game.Instance == null) return;
			if (Grid.WidthInCells == 0 || Grid.HeightInCells == 0) return;

			var gasFlow = Game.Instance.gasConduitFlow;
			var liquidFlow = Game.Instance.liquidConduitFlow;
			if (gasFlow == null && liquidFlow == null) return;

			var ws = WorldStateSyncer.Instance;
			if (ws == null) return;

			// Lazy-init shadow arrays. First pass primes the shadow and emits
			// nothing — avoids broadcasting the whole map on session start.
			if (_shadowGasElement == null)
			{
				_shadowGasElement = new int[Grid.CellCount];
				_shadowGasMass = new float[Grid.CellCount];
				_shadowGasTemp = new float[Grid.CellCount];
				_shadowLiquidElement = new int[Grid.CellCount];
				_shadowLiquidMass = new float[Grid.CellCount];
				_shadowLiquidTemp = new float[Grid.CellCount];
				_lastImmediateEmit = new float[Grid.CellCount];
				PrimeShadow(gasFlow, (int)ObjectLayer.GasConduit, _shadowGasElement, _shadowGasMass, _shadowGasTemp);
				PrimeShadow(liquidFlow, (int)ObjectLayer.LiquidConduit, _shadowLiquidElement, _shadowLiquidMass, _shadowLiquidTemp);
				_lastForceRefresh = Time.unscaledTime;
				return;
			}

			bool forceRefresh = (Time.unscaledTime - _lastForceRefresh) >= FORCE_REFRESH_INTERVAL;
			if (forceRefresh) _lastForceRefresh = Time.unscaledTime;

			var packet = new ConduitContentsPacket();
			int totalSent = 0;
			int pipeCellsScanned = 0;
			int pipeCellsVisible = 0;
			int gasLayer = (int)ObjectLayer.GasConduit;
			int liquidLayer = (int)ObjectLayer.LiquidConduit;

			for (int cell = 0; cell < Grid.CellCount; cell++)
			{
				if (!Grid.IsValidCell(cell)) continue;

				// Cheap pipe presence check — most cells have no pipe.
				bool hasGas = gasFlow != null && Grid.Objects[cell, gasLayer] != null;
				bool hasLiquid = liquidFlow != null && Grid.Objects[cell, liquidLayer] != null;
				if (!hasGas && !hasLiquid) continue;
				pipeCellsScanned++;

				// Visibility gate (invariant #1) — only allocate scratch use
				// once per pipe cell, after the Grid.Objects rejection.
				ws.GetClientsViewingCell(cell, _recipientScratch);
				if (_recipientScratch.Count == 0) continue;
				pipeCellsVisible++;

				if (hasGas)
					MaybeQueueCell(gasFlow, cell, ConduitContentsPacket.CONDUIT_GAS,
						_shadowGasElement, _shadowGasMass, _shadowGasTemp,
						packet, forceRefresh);
				if (hasLiquid)
					MaybeQueueCell(liquidFlow, cell, ConduitContentsPacket.CONDUIT_LIQUID,
						_shadowLiquidElement, _shadowLiquidMass, _shadowLiquidTemp,
						packet, forceRefresh);

				if (packet.Updates.Count >= MAX_UPDATES_PER_PACKET)
				{
					PacketSender.SendToAllClients(packet, PacketSendMode.Unreliable);
					totalSent += packet.Updates.Count;
					packet = new ConduitContentsPacket();
				}
			}

			if (packet.Updates.Count > 0)
			{
				PacketSender.SendToAllClients(packet, PacketSendMode.Unreliable);
				totalSent += packet.Updates.Count;
			}

			if (++_ticksSinceLog >= LOG_EVERY_N_TICKS && totalSent > 0)
			{
				_ticksSinceLog = 0;
				DebugConsole.Log($"[ConduitFlowSyncer] tick: pipes scanned={pipeCellsScanned}, visible={pipeCellsVisible}, updates sent={totalSent}, force={forceRefresh}");
			}
		}

		private static void PrimeShadow(ConduitFlow flow, int objectLayer, int[] shadowEl, float[] shadowMass, float[] shadowTemp)
		{
			if (flow == null) return;
			for (int cell = 0; cell < Grid.CellCount; cell++)
			{
				if (Grid.Objects[cell, objectLayer] == null) continue;
				var c = flow.GetContents(cell);
				shadowEl[cell] = (int)c.element;
				shadowMass[cell] = c.mass;
				shadowTemp[cell] = c.temperature;
			}
		}

		// Emit the cell's contents if it changed since last broadcast, or
		// unconditionally during a force-refresh tick (catches packet drops).
		private static void MaybeQueueCell(ConduitFlow flow, int cell, byte conduitType,
			int[] shadowEl, float[] shadowMass, float[] shadowTemp,
			ConduitContentsPacket packet, bool forceRefresh)
		{
			var c = flow.GetContents(cell);
			int el = (int)c.element;

			if (!forceRefresh)
			{
				bool changed =
					el != shadowEl[cell]
					|| Mathf.Abs(c.mass - shadowMass[cell]) > MASS_THRESHOLD
					|| Mathf.Abs(c.temperature - shadowTemp[cell]) > TEMP_THRESHOLD;
				if (!changed) return;
			}
			else if (c.mass <= 0f && shadowEl[cell] == 0 && shadowMass[cell] <= 0f)
			{
				// Force tick on a cell that has been empty and was last
				// broadcast as empty — no point re-asserting "still empty".
				return;
			}

			shadowEl[cell] = el;
			shadowMass[cell] = c.mass;
			shadowTemp[cell] = c.temperature;

			packet.Updates.Add(new ConduitCellUpdate
			{
				Cell = cell,
				ConduitType = conduitType,
				Element = el,
				Mass = c.mass,
				Temperature = c.temperature,
				DiseaseIdx = c.diseaseIdx,
				DiseaseCount = c.diseaseCount,
			});
		}

		// Client-side apply. Called from ConduitContentsPacket.OnDispatched.
		// Uses the publicised ConduitFlow.SetContents for a direct overwrite —
		// no Remove + Add side effects, no in-flight delta loss.
		public void OnContentsReceived(ConduitContentsPacket packet)
		{
			using var _ = Profiler.Scope();

			if (Game.Instance == null) return;
			if (packet?.Updates == null || packet.Updates.Count == 0) return;

			var gasFlow = Game.Instance.gasConduitFlow;
			var liquidFlow = Game.Instance.liquidConduitFlow;

			foreach (var u in packet.Updates)
			{
				try
				{
					var flow = u.ConduitType == ConduitContentsPacket.CONDUIT_GAS ? gasFlow : liquidFlow;
					if (flow == null) continue;
					if (!Grid.IsValidCell(u.Cell)) continue;
					int layer = u.ConduitType == ConduitContentsPacket.CONDUIT_GAS
						? (int)ObjectLayer.GasConduit
						: (int)ObjectLayer.LiquidConduit;
					if (Grid.Objects[u.Cell, layer] == null) continue; // pipe not built yet on client

					flow.SetContents(u.Cell, new ConduitFlow.ConduitContents(
						(SimHashes)u.Element,
						u.Mass,
						u.Temperature,
						u.DiseaseIdx,
						u.DiseaseCount));
				}
				catch (Exception ex)
				{
					DebugConsole.LogError($"[ConduitFlowSyncer] apply failed for cell {u.Cell}: {ex}");
				}
			}
		}

		// Event-driven fast path: called from the AddElement Harmony Postfix
		// when a cell transitions empty → non-empty on the host. Per-cell rate
		// limit + end-of-frame coalescing keep steady-state flow on the sweep
		// (invariant #2 floor) while letting valve-open and pipe-connect
		// moments land within one Steam P2P RTT.
		public void EnqueueImmediate(int cell, byte conduitType, ConduitFlow.ConduitContents c)
		{
			try
			{
				if (!MultiplayerSession.IsHost) return;
				if (MultiplayerSession.ConnectedPlayers.Count == 0) return;
				if (!_initialized || Time.unscaledTime - _initializationTime < INITIAL_DELAY) return;
				if (_lastImmediateEmit == null) return; // sweep hasn't lazy-init'd yet
				if (!Grid.IsValidCell(cell)) return;

				float now = Time.unscaledTime;
				if (now - _lastImmediateEmit[cell] < IMMEDIATE_RATE_LIMIT) return;
				_lastImmediateEmit[cell] = now;

				// Keep the sweep's shadow in lockstep so the next 1.5 s tick
				// doesn't re-broadcast the same state.
				int el = (int)c.element;
				if (conduitType == ConduitContentsPacket.CONDUIT_GAS)
				{
					_shadowGasElement[cell] = el;
					_shadowGasMass[cell] = c.mass;
					_shadowGasTemp[cell] = c.temperature;
				}
				else
				{
					_shadowLiquidElement[cell] = el;
					_shadowLiquidMass[cell] = c.mass;
					_shadowLiquidTemp[cell] = c.temperature;
				}

				_pendingImmediate.Add(new ConduitCellUpdate
				{
					Cell = cell,
					ConduitType = conduitType,
					Element = el,
					Mass = c.mass,
					Temperature = c.temperature,
					DiseaseIdx = c.diseaseIdx,
					DiseaseCount = c.diseaseCount,
				});
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[ConduitFlowSyncer] EnqueueImmediate failed for cell {cell}: {ex}");
			}
		}

		// End-of-frame flush: coalesces every empty→non-empty event accumulated
		// this frame into a single Reliable packet. Avoids fan-out under bursts
		// (e.g. a reservoir dumping into 50+ cells in one Sim200ms tick).
		private void LateUpdate()
		{
			if (_pendingImmediate.Count == 0) return;
			if (!MultiplayerSession.IsHost) return;

			try
			{
				if (_pendingImmediate.Count <= MAX_UPDATES_PER_PACKET)
				{
					var packet = new ConduitContentsPacket();
					packet.Updates.AddRange(_pendingImmediate);
					PacketSender.SendToAllClients(packet, PacketSendMode.Reliable);
				}
				else
				{
					// Honour the MTU cap even on bursts. Split into multiple
					// Reliable packets rather than one oversize fragmented one.
					for (int i = 0; i < _pendingImmediate.Count; i += MAX_UPDATES_PER_PACKET)
					{
						var packet = new ConduitContentsPacket();
						int end = System.Math.Min(i + MAX_UPDATES_PER_PACKET, _pendingImmediate.Count);
						for (int j = i; j < end; j++) packet.Updates.Add(_pendingImmediate[j]);
						PacketSender.SendToAllClients(packet, PacketSendMode.Reliable);
					}
				}
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[ConduitFlowSyncer] LateUpdate flush failed: {ex}");
			}
			finally
			{
				_pendingImmediate.Clear();
			}
		}
	}
}
