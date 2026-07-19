using System;
using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.DLC.Bionic;
using UnityEngine;

namespace ONI_Together.Patches.DLC.Bionic
{
	internal static class RemoteWorkerDockSync
	{
		private const int MaxPendingStates = 256;
		private const float BroadcastInterval = 0.5f;
		private static readonly Dictionary<int, long> HostRevisions = new();
		private static readonly Dictionary<int, long> AppliedRevisions = new();
		private static readonly Dictionary<int, float> NextBroadcastTimes = new();
		private static readonly Dictionary<int, RemoteWorkerDockStatePacket> PendingStates = new();
		private static System.Runtime.CompilerServices.ConditionalWeakTable<RemoteWorkerSM, DeferredRemoteWorkerActivation> DeferredSpawns = new();
		private static int _applyDepth;

		internal static bool IsApplyingHostState => _applyDepth > 0;

		public static void ResetSessionState()
		{
			HostRevisions.Clear();
			AppliedRevisions.Clear();
			NextBroadcastTimes.Clear();
			PendingStates.Clear();
			DeferredSpawns = new();
			_applyDepth = 0;
		}

		internal static long NextHostRevision(int netId)
			=> NextRevision(HostRevisions, netId);

		internal static bool ShouldRunGameplay(bool inSession, bool isHost) => !inSession || isHost;

		internal static bool IsNewerRevision(long lastRevision, long incomingRevision)
			=> incomingRevision > lastRevision;

		internal static void Broadcast(RemoteWorkerDock dock, bool immediate)
		{
			if (!MultiplayerSession.IsHostInSession || dock == null || dock.gameObject.IsNullOrDestroyed())
				return;
			int dockNetId = EnsureNetId(dock.gameObject);
			if (dockNetId == 0 || !CanBroadcastNow(dockNetId, immediate) ||
			    !TryCapture(dock, dockNetId, out RemoteWorkerDockStatePacket state))
				return;
			PacketSender.SendToAllClients(state, PacketSendMode.ReliableImmediate);
		}

		internal static void BroadcastFromWorker(RemoteWorkerSM worker, bool immediate)
		{
			RemoteWorkerDock dock = worker?.HomeDepot;
			if (dock != null)
				Broadcast(dock, immediate);
		}

		internal static bool PrepareClientWorkerSpawn(RemoteWorkerSM worker)
		{
			if (!MultiplayerSession.IsClient || worker.HomeDepot != null)
				return true;
			int cell = Grid.PosToCell(worker.gameObject);
			foreach (RemoteWorkerDock dock in global::Components.RemoteWorkerDocks.GetItems(worker.GetMyWorldId()))
			{
				if (dock == null || Grid.PosToCell(dock.gameObject) != cell)
					continue;
				ApplyHostState(() =>
				{
					Traverse.Create(dock).Property(nameof(RemoteWorkerDock.RemoteWorker)).SetValue(worker);
					worker.HomeDepot = dock;
				});
				return true;
			}
			DeferredSpawns.GetValue(worker, _ => new DeferredRemoteWorkerActivation()).Defer();
			return false;
		}

		internal static bool TryApplyOrQueue(RemoteWorkerDockStatePacket state)
		{
			if (state == null || !state.IsWireValid())
				return false;
			AppliedRevisions.TryGetValue(state.DockNetId, out long applied);
			if (!IsNewerRevision(applied, state.Revision))
				return true;
			if (!TryApply(state))
			{
				QueuePending(state);
				return false;
			}
			AppliedRevisions[state.DockNetId] = state.Revision;
			PendingStates.Remove(state.DockNetId);
			return true;
		}

		internal static void RetryPending()
		{
			foreach (RemoteWorkerDockStatePacket state in new List<RemoteWorkerDockStatePacket>(PendingStates.Values))
				TryApplyOrQueue(state);
		}

		internal static void Cleanup(int dockNetId)
		{
			HostRevisions.Remove(dockNetId);
			AppliedRevisions.Remove(dockNetId);
			NextBroadcastTimes.Remove(dockNetId);
			PendingStates.Remove(dockNetId);
		}

		private static bool TryCapture(RemoteWorkerDock dock, int dockNetId,
			out RemoteWorkerDockStatePacket state)
		{
			RemoteWorkerSM worker = dock.RemoteWorker;
			int workerNetId = worker == null ? 0 : EnsureNetId(worker.gameObject);
			if (worker != null && workerNetId == 0)
			{
				state = null;
				return false;
			}
			var terminal = Traverse.Create(dock).Field("terminal").GetValue<RemoteWorkTerminal>();
			state = CaptureBase(dock, dockNetId, worker, workerNetId, terminal);
			CaptureWorker(state, worker);
			return state.IsWireValid();
		}

		private static RemoteWorkerDockStatePacket CaptureBase(RemoteWorkerDock dock, int dockNetId,
			RemoteWorkerSM worker, int workerNetId, RemoteWorkTerminal terminal)
		{
			var state = new RemoteWorkerDockStatePacket
			{
				DockNetId = dockNetId,
				Revision = NextHostRevision(dockNetId),
				WorkerNetId = workerNetId,
				TerminalNetId = terminal == null ? 0 : EnsureNetId(terminal.gameObject)
			};
			Storage storage = dock.GetComponent<Storage>();
			state.DockMaterial = CaptureStorage(storage, RemoteWorkerConfig.BUILD_MATERIAL_TAG);
			state.DockOil = CaptureStorage(storage, GameTags.LubricatingOil);
			state.DockGunk = CaptureStorage(storage, SimHashes.LiquidGunk.CreateTag());
			return state;
		}

		private static void CaptureWorker(RemoteWorkerDockStatePacket state, RemoteWorkerSM worker)
		{
			if (worker == null)
				return;
			state.Docked = worker.Docked;
			state.PlayNewWorker = worker.playNewWorker;
			state.ActivelyControlled = worker.ActivelyControlled;
			state.ActivelyWorking = worker.ActivelyWorking;
			state.Available = worker.Available;
			state.WorkerPrimary = CaptureElement(worker.GetComponent<PrimaryElement>());
			state.Charge = worker.GetComponent<RemoteWorkerCapacitor>()?.Charge ?? 0f;
			Storage storage = worker.GetComponent<Storage>();
			state.WorkerOil = CaptureStorage(storage, GameTags.LubricatingOil);
			state.WorkerGunk = CaptureStorage(storage, SimHashes.LiquidGunk.CreateTag());
		}

		private static bool TryApply(RemoteWorkerDockStatePacket state)
		{
			if (!NetworkIdentityRegistry.TryGetComponent(state.DockNetId, out RemoteWorkerDock dock))
				return false;
			RemoteWorkerSM worker = null;
			RemoteWorkTerminal terminal = null;
			if (state.WorkerNetId != 0 &&
			    !NetworkIdentityRegistry.TryGetComponent(state.WorkerNetId, out worker))
				return false;
			if (state.TerminalNetId != 0 &&
			    !NetworkIdentityRegistry.TryGetComponent(state.TerminalNetId, out terminal))
				return false;
			if (!RelationshipsAreLocal(dock, worker, terminal))
				return false;
			ApplyHostState(() =>
			{
					ApplyDockResources(dock, state);
					ApplyWorker(worker, state);
					ApplyRelationships(dock, worker, terminal);
					ApplyWorker(worker, state);
				});
			return true;
		}

		private static void ApplyHostState(System.Action action)
		{
			_applyDepth++;
			try
			{
				action();
			}
			finally
			{
				_applyDepth--;
			}
		}

		private static void ApplyDockResources(RemoteWorkerDock dock, RemoteWorkerDockStatePacket state)
		{
			Storage storage = dock.GetComponent<Storage>();
			ApplyStorage(storage, RemoteWorkerConfig.BUILD_MATERIAL_TAG, state.DockMaterial);
			ApplyStorage(storage, GameTags.LubricatingOil, state.DockOil);
			ApplyStorage(storage, SimHashes.LiquidGunk.CreateTag(), state.DockGunk);
		}

		private static void ApplyWorker(RemoteWorkerSM worker, RemoteWorkerDockStatePacket state)
		{
			if (worker == null)
				return;
			ApplyElement(worker.GetComponent<PrimaryElement>(), state.WorkerPrimary, applyMass: true);
			Traverse.Create(worker.GetComponent<RemoteWorkerCapacitor>()).Field("charge").SetValue(state.Charge);
			Storage storage = worker.GetComponent<Storage>();
			ApplyStorage(storage, GameTags.LubricatingOil, state.WorkerOil);
			ApplyStorage(storage, SimHashes.LiquidGunk.CreateTag(), state.WorkerGunk);
			worker.Docked = state.Docked;
			worker.playNewWorker = state.PlayNewWorker;
			worker.ActivelyControlled = state.ActivelyControlled;
			worker.ActivelyWorking = state.ActivelyWorking;
			worker.Available = state.Available;
		}

		private static void ApplyRelationships(RemoteWorkerDock dock, RemoteWorkerSM worker,
			RemoteWorkTerminal terminal)
		{
			RemoteWorkerSM previous = dock.RemoteWorker;
			if (previous != null && previous != worker && previous.HomeDepot == dock)
				previous.HomeDepot = null;
			Traverse.Create(dock).Property(nameof(RemoteWorkerDock.RemoteWorker)).SetValue(worker);
			Traverse.Create(dock).Field("terminal").SetValue(terminal);
			if (worker != null && worker.HomeDepot != dock)
				worker.HomeDepot = dock;
			if (terminal != null && terminal.CurrentDock != dock)
				terminal.CurrentDock = dock;
			TryActivateDeferredSpawn(worker);
		}

		private static void TryActivateDeferredSpawn(RemoteWorkerSM worker)
		{
			if (worker == null || !DeferredSpawns.TryGetValue(worker, out DeferredRemoteWorkerActivation activation))
				return;
			bool ready = worker.HomeDepot != null && worker.HomeDepot.RemoteWorker == worker;
			activation.TryActivate(ready,
				() => RemoteWorkerStateMachineSpawnPatch.RunOriginalOnSpawn(worker));
		}

		private static RemoteWorkerElementState CaptureStorage(Storage storage, Tag tag)
		{
			if (storage == null)
				return new RemoteWorkerElementState();
			float total = 0f;
			PrimaryElement representative = null;
			foreach (GameObject item in storage.items)
			{
				if (item == null || !item.HasTag(tag) ||
				    !item.TryGetComponent(out PrimaryElement element) || element.Mass <= 0f)
					continue;
				representative ??= element;
				total += element.Mass;
			}
			RemoteWorkerElementState state = CaptureElement(representative);
			state.Mass = total;
			return state;
		}

		private static RemoteWorkerElementState CaptureElement(PrimaryElement element)
		{
			if (element == null || element.Mass <= 0f)
				return new RemoteWorkerElementState();
			return new RemoteWorkerElementState
			{
				Present = true,
				ElementId = (int)element.ElementID,
				Mass = element.Mass,
				Temperature = element.Temperature,
				DiseaseIndex = element.DiseaseIdx,
				DiseaseCount = element.DiseaseCount
			};
		}

		private static void ApplyStorage(Storage storage, Tag tag, RemoteWorkerElementState state)
		{
			if (storage == null)
				return;
			float remaining = state.Present ? state.Mass : 0f;
			for (int i = storage.items.Count - 1; i >= 0; i--)
			{
				GameObject item = storage.items[i];
				if (item == null || !item.HasTag(tag) || !item.TryGetComponent(out PrimaryElement element))
					continue;
				float kept = Math.Min(element.Mass, remaining);
				remaining -= kept;
				element.Mass = kept;
			}
			PrimaryElement representative = FindFirst(storage, tag);
			if (remaining > 0f)
				representative = AddOrGrow(storage, representative, state, remaining);
			if (representative != null && state.Present)
				ApplyElement(representative, state, applyMass: false);
		}

		private static PrimaryElement AddOrGrow(Storage storage, PrimaryElement representative,
			RemoteWorkerElementState state, float remaining)
		{
			if (representative != null)
			{
				representative.Mass += remaining;
				return representative;
			}
			return storage.AddElement((SimHashes)state.ElementId, remaining, state.Temperature,
				state.DiseaseIndex, state.DiseaseCount, keep_zero_mass: false);
		}

		private static PrimaryElement FindFirst(Storage storage, Tag tag)
		{
			foreach (GameObject item in storage.items)
				if (item != null && item.HasTag(tag) && item.TryGetComponent(out PrimaryElement element) &&
				    element.Mass > 0f)
					return element;
			return null;
		}

		private static void ApplyElement(PrimaryElement element, RemoteWorkerElementState state,
			bool applyMass)
		{
			if (element == null || !state.Present)
				return;
			element.ElementID = (SimHashes)state.ElementId;
			if (applyMass)
				element.Mass = state.Mass;
			element.Temperature = state.Temperature;
			if (element.DiseaseIdx == state.DiseaseIndex && element.DiseaseCount == state.DiseaseCount)
				return;
			if (element.DiseaseCount > 0)
				element.ModifyDiseaseCount(-element.DiseaseCount, "RemoteWorkerDock host state");
			if (state.DiseaseCount > 0)
				element.AddDisease(state.DiseaseIndex, state.DiseaseCount, "RemoteWorkerDock host state");
		}

		private static bool RelationshipsAreLocal(RemoteWorkerDock dock, RemoteWorkerSM worker,
			RemoteWorkTerminal terminal)
		{
			int worldId = dock.GetMyWorldId();
			return (worker == null || worker.GetMyWorldId() == worldId) &&
			       (terminal == null || terminal.GetMyWorldId() == worldId);
		}

		private static bool CanBroadcastNow(int netId, bool immediate)
		{
			if (!immediate && NextBroadcastTimes.TryGetValue(netId, out float next) && Time.unscaledTime < next)
				return false;
			NextBroadcastTimes[netId] = Time.unscaledTime + BroadcastInterval;
			return true;
		}

		private static void QueuePending(RemoteWorkerDockStatePacket state)
		{
			if (PendingStates.Count >= MaxPendingStates && !PendingStates.ContainsKey(state.DockNetId))
			{
				using var enumerator = PendingStates.Keys.GetEnumerator();
				if (enumerator.MoveNext())
					PendingStates.Remove(enumerator.Current);
			}
			if (!PendingStates.TryGetValue(state.DockNetId, out var previous) || state.Revision > previous.Revision)
				PendingStates[state.DockNetId] = state;
		}

		private static int EnsureNetId(GameObject go)
		{
			var identity = go?.AddOrGet<Networking.Components.NetworkIdentity>();
			identity?.RegisterIdentity();
			return identity?.NetId ?? 0;
		}

		private static long NextRevision(Dictionary<int, long> revisions, int netId)
		{
			revisions.TryGetValue(netId, out long previous);
			long next = previous == long.MaxValue ? 1 : previous + 1;
			revisions[netId] = next;
			return next;
		}
	}

	internal static class RemoteWorkerDockSelectionSync
	{
		private static readonly Dictionary<int, long> HostRevisions = new();
		private static readonly Dictionary<int, long> AppliedRevisions = new();
		private static int _applyDepth;
		internal static bool IsApplying => _applyDepth > 0;

		public static void ResetSessionState()
		{
			HostRevisions.Clear();
			AppliedRevisions.Clear();
			_applyDepth = 0;
		}

		internal static long NextHostRevision(int terminalNetId)
		{
			HostRevisions.TryGetValue(terminalNetId, out long previous);
			long next = previous == long.MaxValue ? 1 : previous + 1;
			HostRevisions[terminalNetId] = next;
			return next;
		}

		internal static bool TryApplyRequest(RemoteWorkerDockSelectionRequestPacket request,
			out RemoteWorkerDockSelectionStatePacket state)
		{
			state = null;
			if (request == null || !request.IsWireValid() || !TryResolve(request.TerminalNetId, out var terminal))
				return false;
			int current = GetNetId(terminal.FutureDock);
			if (current != request.ExpectedDockNetId ||
			    !TryResolveDock(request.DesiredDockNetId, terminal.GetMyWorldId(), out var desired))
				return false;
			Apply(terminal, desired);
			state = Capture(terminal, request.TerminalNetId);
			return true;
		}

		internal static bool TryApply(RemoteWorkerDockSelectionStatePacket state)
		{
			if (state == null || !state.IsWireValid() || !TryResolve(state.TerminalNetId, out var terminal))
				return false;
			AppliedRevisions.TryGetValue(state.TerminalNetId, out long applied);
			if (!RemoteWorkerDockSync.IsNewerRevision(applied, state.Revision))
				return true;
			if (!TryResolveDock(state.DockNetId, terminal.GetMyWorldId(), out var dock))
				return false;
			Apply(terminal, dock);
			AppliedRevisions[state.TerminalNetId] = state.Revision;
			return true;
		}

		internal static void SendRequest(RemoteWorkTerminal terminal, RemoteWorkerDock desired)
		{
			int terminalNetId = GetNetId(terminal);
			int expected = GetNetId(terminal?.FutureDock);
			int wanted = GetNetId(desired);
			var request = new RemoteWorkerDockSelectionRequestPacket
			{
				TerminalNetId = terminalNetId,
				ExpectedDockNetId = expected,
				DesiredDockNetId = wanted
			};
			if (request.IsWireValid())
				PacketSender.SendToAllOtherPeers(request);
		}

		internal static void Broadcast(RemoteWorkTerminal terminal)
		{
			if (!MultiplayerSession.IsHostInSession || terminal == null)
				return;
			int terminalNetId = GetNetId(terminal);
			if (terminalNetId != 0)
				PacketSender.SendToAllClients(Capture(terminal, terminalNetId));
		}

		private static RemoteWorkerDockSelectionStatePacket Capture(RemoteWorkTerminal terminal,
			int terminalNetId)
			=> new()
			{
				TerminalNetId = terminalNetId,
				DockNetId = GetNetId(terminal.FutureDock),
				Revision = NextHostRevision(terminalNetId)
			};

		private static void Apply(RemoteWorkTerminal terminal, RemoteWorkerDock dock)
		{
			_applyDepth++;
			try
			{
				terminal.FutureDock = dock;
			}
			finally
			{
				_applyDepth--;
			}
		}

		private static bool TryResolve(int netId, out RemoteWorkTerminal terminal)
			=> NetworkIdentityRegistry.TryGetComponent(netId, out terminal);

		private static bool TryResolveDock(int netId, int worldId, out RemoteWorkerDock dock)
		{
			dock = null;
			return netId == 0 || NetworkIdentityRegistry.TryGetComponent(netId, out dock) &&
			       dock.GetMyWorldId() == worldId;
		}

		private static int GetNetId(Component component)
		{
			if (component == null)
				return 0;
			var identity = component.gameObject.AddOrGet<Networking.Components.NetworkIdentity>();
			identity.RegisterIdentity();
			return identity.NetId;
		}

	}

}
