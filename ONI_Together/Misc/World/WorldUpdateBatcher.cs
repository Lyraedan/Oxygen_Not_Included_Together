using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;
using System;
using System.Collections.Generic;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Misc.World
{
	public static partial class WorldUpdateBatcher
	{
		private const float FlushInterval = 10f;
		private const int PacketHeaderSize = 40;
		private const float BytesPerUpdate = 7.38f;
		private const int MinimumPendingRepairCells = 65536;
		private const int MaxPendingRepairDispatchPackets = 256;
		private const float TargetDispatchPacketsPerSecond = 160f;
		private const int MaxDispatchPacketsPerFrame = 64;
		private static readonly object Gate = new();
		private static readonly List<WorldUpdatePacket.CellUpdate> pendingUpdates = new();
		private static readonly Dictionary<int, WorldUpdatePacket.CellUpdate> pendingRepairUpdates = new();
		private static readonly Queue<WorldUpdatePacket> foregroundDispatch = new();
		private static readonly Queue<WorldUpdatePacket> repairDispatch = new();
		private static readonly WorldUpdateRepairJournal repairJournal = new();
		private static bool worldDispatchFrozen;
		private static bool frozenCheckpointValid;
		private static bool journalBackpressureLogged;
		private static int inFlightDispatches;
		private static long mutationVersion;
		private static long frozenMutationVersion;
		private static float flushTimer;

		public static bool HasPendingDispatch
		{
			get
			{
				lock (Gate)
					return foregroundDispatch.Count != 0 || repairDispatch.Count != 0;
			}
		}

		public static void ResetSessionState()
		{
			lock (Gate)
			{
				pendingUpdates.Clear();
				pendingRepairUpdates.Clear();
				foregroundDispatch.Clear();
				repairDispatch.Clear();
				worldDispatchFrozen = false;
				frozenCheckpointValid = false;
				journalBackpressureLogged = false;
				mutationVersion = 0;
				frozenMutationVersion = 0;
				flushTimer = 0f;
				repairJournal.Reset();
				WorldUpdatePacket.ResetRevisionState();
			}
		}

		public static bool Queue(
			WorldUpdatePacket.CellUpdate update, bool backgroundRepair = false)
		{
			using var _ = Profiler.Scope();
			if (MultiplayerSession.IsClient || !Grid.IsValidCell(update.Cell))
				return false;
			lock (Gate)
				return QueueLocked(update, backgroundRepair);
		}

		internal static bool QueueForTests(
			WorldUpdatePacket.CellUpdate update, bool backgroundRepair = false)
		{
			lock (Gate)
				return QueueLocked(update, backgroundRepair);
		}

		private static bool QueueLocked(
			WorldUpdatePacket.CellUpdate update, bool backgroundRepair)
		{
			if (backgroundRepair && worldDispatchFrozen)
				return false;
			if (!backgroundRepair)
			{
				pendingUpdates.Add(update);
				MarkMutationLocked();
				return true;
			}
			int stagingCapacity = RepairStagingCapacity(Grid.CellCount);
			if (pendingRepairUpdates.ContainsKey(update.Cell)
			    || pendingRepairUpdates.Count < stagingCapacity)
			{
				pendingRepairUpdates[update.Cell] = update;
				MarkMutationLocked();
				return true;
			}
			else
			{
				DebugConsole.LogError(
					$"[WorldUpdateBatcher] Repair staging capacity {stagingCapacity} reached; retaining existing cells.",
					false);
				return false;
			}
		}

		internal static int PendingCountForTests(bool backgroundRepair)
		{
			lock (Gate)
				return backgroundRepair ? pendingRepairUpdates.Count : pendingUpdates.Count;
		}

		public static void Update()
		{
			using var _ = Profiler.Scope();
			if (MultiplayerSession.IsClient)
				return;
			float deltaTime = Time.unscaledDeltaTime;
			if (AdvanceFlushTimer(deltaTime))
				Flush();
			RefillDispatchQueues();
			int freshBudget = DispatchBudgetForFrame(deltaTime);
			int freshSent = DispatchForegroundPackets(freshBudget);
			if (freshSent == freshBudget)
				return;
			float now = Time.unscaledTime;
			int totalSent = freshSent;
			if (SendOneFreshRepair(now))
			{
				freshSent++;
				totalSent++;
			}
			if (totalSent < MaxDispatchPacketsPerFrame && CanReplayRepair()
			    && repairJournal.ReplayOneDue(now, SendPeriodicRepairReplay))
				totalSent++;
			while (freshSent < freshBudget && totalSent < MaxDispatchPacketsPerFrame
			       && SendOneFreshRepair(now))
			{
				freshSent++;
				totalSent++;
			}
		}

		private static bool AdvanceFlushTimer(float deltaTime)
		{
			lock (Gate)
			{
				flushTimer += deltaTime;
				if (flushTimer < FlushInterval)
					return false;
				flushTimer = 0f;
				return true;
			}
		}

		public static int Flush()
		{
			using var _ = Profiler.Scope();
			if (MultiplayerSession.IsClient)
				return 0;
			lock (Gate)
				return PackagePending();
		}

		internal static bool TryTakePendingDispatch(
			out WorldUpdatePacket packet, out PacketSendMode mode,
			bool requireReadyClients = true)
		{
			if (TryTakeForegroundDispatch(out packet, out mode))
				return true;
			var request = new RepairDispatchRequest(
				requireReadyClients, dispatchTime: 0f, trackInFlight: false);
			return TryTakeRepairDispatch(
				out packet, out mode, request);
		}

		private static bool TryTakeForegroundDispatch(
			out WorldUpdatePacket packet, out PacketSendMode mode,
			bool trackInFlight = false)
		{
			lock (Gate)
			{
				if (!worldDispatchFrozen && foregroundDispatch.Count != 0)
				{
					packet = foregroundDispatch.Dequeue();
					MarkMutationLocked();
					if (trackInFlight)
						inFlightDispatches++;
					mode = PacketSendMode.Reliable;
					return true;
				}
				packet = null;
				mode = default;
				return false;
			}
		}

		private static bool TryTakeRepairDispatch(
			out WorldUpdatePacket packet, out PacketSendMode mode,
			RepairDispatchRequest request)
		{
			lock (Gate)
			{
				bool canDispatchRepair = !worldDispatchFrozen && (!request.RequireReadyClients
				    || (!ReadyManager.HasActiveSyncBarrier && ReadyManager.IsEveryoneReady()));
				if (repairDispatch.Count != 0 && canDispatchRepair)
				{
					packet = repairDispatch.Peek();
					if (!TryRecordRepairLocked(packet, request.DispatchTime))
					{
						packet = null;
						mode = default;
						return false;
					}
					repairDispatch.Dequeue();
					MarkMutationLocked();
					if (request.TrackInFlight)
						inFlightDispatches++;
					mode = PacketSendMode.Unreliable;
					return true;
				}
				packet = null;
				mode = default;
				return false;
			}
		}

		private static bool TryRecordRepairLocked(
			WorldUpdatePacket packet, float dispatchTime)
		{
			if (repairJournal.TryRecordNext(
				    packet, ReadyRemoteClientIdsLocked(), dispatchTime))
			{
				journalBackpressureLogged = false;
				return true;
			}
			if (!journalBackpressureLogged)
			{
				journalBackpressureLogged = true;
				DebugConsole.LogError(
					"[WorldUpdateBatcher] Repair journal capacity reached; dispatch is backpressured until cumulative ACK advances.",
					false);
			}
			return false;
		}

		internal static bool TryFreezeRepairDispatch(out long repairSequenceCut)
		{
			if (!TryFreezeWorldDispatch(
				    out repairSequenceCut, out long checkpointVersion))
				return false;
			if (repairJournal.ReplayPendingThrough(
				    repairSequenceCut, SendFrozenRepairReplay)
			    && IsFrozenCheckpointValid(checkpointVersion))
				return true;
			ResumeRepairDispatch();
			repairSequenceCut = 0;
			return false;
		}

		internal static bool TryFreezeWorldDispatch(
			out long repairSequenceCut, out long checkpointVersion)
		{
			lock (Gate)
			{
				if (worldDispatchFrozen || inFlightDispatches != 0
				    || pendingUpdates.Count != 0
				    || pendingRepairUpdates.Count != 0 || foregroundDispatch.Count != 0
				    || repairDispatch.Count != 0)
				{
					repairSequenceCut = 0;
					checkpointVersion = 0;
					return false;
				}
				worldDispatchFrozen = true;
				frozenCheckpointValid = true;
				frozenMutationVersion = mutationVersion;
				repairSequenceCut = WorldUpdatePacket.CurrentHostRepairDispatchSequence;
				checkpointVersion = frozenMutationVersion;
				return true;
			}
		}

		internal static bool IsFrozenCheckpointValid(long checkpointVersion)
		{
			lock (Gate)
				return worldDispatchFrozen && frozenCheckpointValid
				       && checkpointVersion == frozenMutationVersion
				       && checkpointVersion == mutationVersion;
		}

		internal static bool IsFrozenCheckpointValid()
		{
			lock (Gate)
				return worldDispatchFrozen && frozenCheckpointValid
				       && frozenMutationVersion == mutationVersion;
		}

		internal static void ResumeRepairDispatch()
		{
			lock (Gate)
			{
				worldDispatchFrozen = false;
				frozenCheckpointValid = false;
				frozenMutationVersion = 0;
			}
		}

		internal static bool RepairDispatchPausedForTests
		{
			get { lock (Gate) return worldDispatchFrozen; }
		}

		internal static long RepairRetransmitCount => repairJournal.RetransmitCount;

		internal static bool RepairJournalBackpressured => repairJournal.IsBackpressured;

		internal static bool HasFreshRepairBudget(
			bool foregroundDispatched, bool replayed)
			=> !foregroundDispatched;

		internal static void AcceptRepairAck(ulong clientId, long appliedThrough)
		{
			bool accepted;
			lock (Gate)
				accepted = repairJournal.AcceptAppliedAck(clientId, appliedThrough);
			if (!accepted)
				DebugConsole.LogWarning(
					$"[WorldUpdateBatcher] Rejected repair ACK {appliedThrough} from {clientId}");
		}

		internal static void DropRepairClient(ulong clientId)
		{
			int released;
			lock (Gate)
				released = repairJournal.DropClient(clientId);
			if (released > 0)
				DebugConsole.Log(
					$"[WorldUpdateBatcher] Released {released} repair journal entries for disconnected client {clientId}");
		}

		private static bool CanReplayRepair()
		{
			lock (Gate)
				return !worldDispatchFrozen;
		}

		private static bool SendPeriodicRepairReplay(
			ulong clientId, WorldUpdatePacket packet)
		{
			if (!TryBeginInFlightDispatch())
				return false;
			try
			{
				return SendRepairReplayCore(clientId, packet);
			}
			finally
			{
				CompleteInFlightDispatch();
			}
		}

		private static bool SendFrozenRepairReplay(
			ulong clientId, WorldUpdatePacket packet)
		{
			if (!TryBeginFrozenReplay())
				return false;
			try
			{
				return SendRepairReplayCore(clientId, packet);
			}
			finally
			{
				CompleteInFlightDispatch();
			}
		}

		private static bool SendRepairReplayCore(
			ulong clientId, WorldUpdatePacket packet)
		{
			if (!MultiplayerSession.ConnectedPlayers.TryGetValue(
				    clientId, out MultiplayerPlayer player)
			    || player.Connection == null || !player.ProtocolVerified
			    || !SyncBarrier.IsExactReady(player.readyState))
				return false;
			return PacketSender.SendToPlayer(
				clientId, packet, PacketSendMode.ReliableImmediate);
		}

		private static bool TryBeginInFlightDispatch()
		{
			lock (Gate)
			{
				if (worldDispatchFrozen)
					return false;
				inFlightDispatches++;
				return true;
			}
		}

		private static bool TryBeginFrozenReplay()
		{
			lock (Gate)
			{
				if (!worldDispatchFrozen || !frozenCheckpointValid)
					return false;
				inFlightDispatches++;
				return true;
			}
		}

		private static void CompleteInFlightDispatch()
		{
			lock (Gate)
			{
				if (inFlightDispatches <= 0)
					throw new InvalidOperationException("World dispatch lease underflow");
				inFlightDispatches--;
			}
		}

		internal static bool TryBeginWorldDispatchForTests()
			=> TryBeginInFlightDispatch();

		internal static void CompleteWorldDispatchForTests()
			=> CompleteInFlightDispatch();

		private static List<ulong> ReadyRemoteClientIdsLocked()
		{
			var clients = new List<ulong>();
			foreach (MultiplayerPlayer player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (player.PlayerId != MultiplayerSession.HostUserID
				    && player.Connection != null && player.ProtocolVerified
				    && SyncBarrier.IsExactReady(player.readyState))
					clients.Add(player.PlayerId);
			}
			return clients;
		}

		private static void MarkMutationLocked()
		{
			mutationVersion = mutationVersion == long.MaxValue ? 1 : mutationVersion + 1;
			if (worldDispatchFrozen)
				frozenCheckpointValid = false;
		}

		private readonly struct RepairDispatchRequest
		{
			internal readonly bool RequireReadyClients;
			internal readonly float DispatchTime;
			internal readonly bool TrackInFlight;

			internal RepairDispatchRequest(
				bool requireReadyClients, float dispatchTime, bool trackInFlight)
			{
				RequireReadyClients = requireReadyClients;
				DispatchTime = dispatchTime;
				TrackInFlight = trackInFlight;
			}
		}
	}
}
