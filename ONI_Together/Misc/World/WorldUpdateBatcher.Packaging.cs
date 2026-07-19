using System;
using System.Collections.Generic;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.Misc.World
{
	public static partial class WorldUpdateBatcher
	{
		internal static int RepairStagingCapacity(int gridCellCount)
			=> Math.Max(MinimumPendingRepairCells, Math.Max(0, gridCellCount));

		internal static int DispatchBudgetForFrame(float deltaTime)
			=> UnityEngine.Mathf.Clamp(UnityEngine.Mathf.CeilToInt(
				TargetDispatchPacketsPerSecond * deltaTime), 1, MaxDispatchPacketsPerFrame);

		private static int DispatchForegroundPackets(int budget)
		{
			int sent = 0;
			while (sent < budget && TryTakeForegroundDispatch(
				       out WorldUpdatePacket packet, out PacketSendMode mode,
				       trackInFlight: true))
			{
				SendLeasedPacket(packet, mode);
				sent++;
			}
			return sent;
		}

		private static void RefillDispatchQueues()
		{
			lock (Gate)
			{
				if (pendingRepairUpdates.Count != 0
				    && repairDispatch.Count < MaxPendingRepairDispatchPackets)
					PackagePending();
			}
		}

		internal static void RefillDispatchQueuesForTests()
			=> RefillDispatchQueues();

		internal static int PackagePendingForTests()
		{
			lock (Gate)
				return PackagePending();
		}

		internal static int PendingRepairDispatchCountForTests
		{
			get { lock (Gate) return repairDispatch.Count; }
		}

		internal static bool IsRepairPipelineIdle
		{
			get
			{
				lock (Gate)
					return RepairPipelineCountsAreIdle(
						pendingRepairUpdates.Count,
						repairDispatch.Count,
						repairJournal.PendingEntryCount);
			}
		}

		internal static bool RepairPipelineCountsAreIdle(
			int staged, int dispatch, int journal)
			=> staged == 0 && dispatch == 0 && journal == 0;

		internal static int RepairJournalPendingCount
		{
			get { lock (Gate) return repairJournal.PendingEntryCount; }
		}

		internal static int MaxRepairDispatchPacketsForTests
			=> MaxPendingRepairDispatchPackets;

		internal static int MaxRepairUpdatesPerPacketForTests
			=> MaxUpdatesPerPacket(backgroundRepair: true);

		internal static int RepairProducerCellBudget(
			int requestedCells, int gridCellCount)
		{
			lock (Gate)
				return RepairProducerCellBudgetLocked(requestedCells, gridCellCount);
		}

		internal static int RepairProducerCellBudgetForTests(
			int requestedCells, int gridCellCount)
			=> RepairProducerCellBudget(requestedCells, gridCellCount);

		private static int RepairProducerCellBudgetLocked(
			int requestedCells, int gridCellCount)
		{
			if (requestedCells <= 0 || gridCellCount <= 0 || worldDispatchFrozen)
				return 0;
			int cellsPerPacket = MaxUpdatesPerPacket(backgroundRepair: true);
			int stagedCells = pendingRepairUpdates.Count;
			int stagedPackets = DivideRoundUp(stagedCells, cellsPerPacket);
			int committedPackets = repairJournal.PendingEntryCount
			                       + repairDispatch.Count + stagedPackets;
			int packetSlots = Math.Max(
				0, WorldUpdateRepairJournal.DefaultMaxEntries - committedPackets);
			int partialPacketCells = stagedCells == 0 || stagedCells % cellsPerPacket == 0
				? 0
				: cellsPerPacket - stagedCells % cellsPerPacket;
			int packetHeadroom = committedPackets > WorldUpdateRepairJournal.DefaultMaxEntries
				? 0
				: packetSlots * cellsPerPacket + partialPacketCells;
			int pipelineUpdates = repairJournal.PendingUpdateCount
			                      + RepairDispatchUpdateCountLocked() + stagedCells;
			int updateHeadroom = Math.Max(
				0, WorldUpdateRepairJournal.DefaultMaxUpdates - pipelineUpdates);
			int stagingHeadroom = Math.Max(
				0, RepairStagingCapacity(gridCellCount) - stagedCells);
			return Math.Min(requestedCells,
				Math.Min(packetHeadroom, Math.Min(updateHeadroom, stagingHeadroom)));
		}

		private static int RepairDispatchUpdateCountLocked()
		{
			int updates = 0;
			foreach (WorldUpdatePacket packet in repairDispatch)
				updates += packet.Updates.Count;
			return updates;
		}

		private static int DivideRoundUp(int value, int divisor)
			=> value == 0 ? 0 : (value - 1) / divisor + 1;

		private static bool SendOneFreshRepair(float dispatchTime)
		{
			var request = new RepairDispatchRequest(true, dispatchTime, true);
			if (!TryTakeRepairDispatch(
				    out WorldUpdatePacket packet, out PacketSendMode mode, request))
				return false;
			SendLeasedPacket(packet, mode);
			return true;
		}

		private static void SendLeasedPacket(
			WorldUpdatePacket packet, PacketSendMode mode)
		{
			try
			{
				PacketSender.SendToAllClients(packet, mode);
			}
			finally
			{
				CompleteInFlightDispatch();
			}
		}

		private static int PackagePending()
		{
			int foregroundCount = pendingUpdates.Count;
			if (foregroundCount == 0 && pendingRepairUpdates.Count == 0)
				return 0;
			List<WorldUpdatePacket> foreground = BuildPackets(pendingUpdates, false);
			int available = MaxPendingRepairDispatchPackets - repairDispatch.Count;
			List<WorldUpdatePacket> repairs = BuildRepairPacketPrefix(
				pendingRepairUpdates.Values, available, out List<int> repairCells);
			if (pendingRepairUpdates.Count != 0 && repairCells.Count == 0)
				DebugConsole.LogError(
					$"[WorldUpdateBatcher] Repair dispatch capacity {MaxPendingRepairDispatchPackets} reached; applying backpressure.",
					false);
			long foregroundCut = WorldUpdatePacket.CurrentHostForegroundSequence;
			foreach (WorldUpdatePacket packet in foreground)
				packet.Revision = WorldUpdatePacket.NextHostRevision();
			foreach (WorldUpdatePacket repair in repairs)
			{
				repair.ForegroundCut = foregroundCut;
				repair.Revision = WorldUpdatePacket.NextHostRevision();
			}
			EnqueueDispatch(foreground, repairs);
			pendingUpdates.Clear();
			foreach (int cell in repairCells)
				pendingRepairUpdates.Remove(cell);
			return (int)((foregroundCount + repairCells.Count) * BytesPerUpdate);
		}

		private static List<WorldUpdatePacket> BuildPackets(
			IEnumerable<WorldUpdatePacket.CellUpdate> updates, bool backgroundRepair)
		{
			var packets = new List<WorldUpdatePacket>();
			int maxUpdates = MaxUpdatesPerPacket(backgroundRepair);
			WorldUpdatePacket packet = null;
			foreach (WorldUpdatePacket.CellUpdate update in updates)
			{
				if (packet == null || packet.Updates.Count >= maxUpdates)
				{
					packet = new WorldUpdatePacket
					{
						Sequence = backgroundRepair
							? 0
							: WorldUpdatePacket.NextHostForegroundSequence(),
					};
					packets.Add(packet);
				}
				packet.Updates.Add(update);
			}
			return packets;
		}

		private static List<WorldUpdatePacket> BuildRepairPacketPrefix(
			IEnumerable<WorldUpdatePacket.CellUpdate> updates,
			int maxPackets,
			out List<int> packagedCells)
		{
			var packets = new List<WorldUpdatePacket>();
			packagedCells = new List<int>();
			int maxUpdates = MaxUpdatesPerPacket(backgroundRepair: true);
			WorldUpdatePacket packet = null;
			foreach (WorldUpdatePacket.CellUpdate update in updates)
			{
				if (packet == null || packet.Updates.Count >= maxUpdates)
				{
					if (packets.Count >= maxPackets)
						break;
					packet = new WorldUpdatePacket();
					packets.Add(packet);
				}
				packet.Updates.Add(update);
				packagedCells.Add(update.Cell);
			}
			return packets;
		}

		private static int MaxUpdatesPerPacket(bool backgroundRepair)
		{
			float maxBytes = NetworkConfig.IsLanConfig()
				? PacketSender.MAX_PACKET_SIZE_LAN * 1024
				: backgroundRepair
					? PacketSender.MAX_PACKET_SIZE_UNRELIABLE
					: PacketSender.MAX_PACKET_SIZE_RELIABLE;
			return Math.Max(1, (int)((maxBytes - PacketHeaderSize) / BytesPerUpdate));
		}

		private static void EnqueueDispatch(
			IEnumerable<WorldUpdatePacket> foreground,
			IEnumerable<WorldUpdatePacket> repairs)
		{
			foreach (WorldUpdatePacket packet in foreground)
				foregroundDispatch.Enqueue(packet);
			foreach (WorldUpdatePacket repair in repairs)
				repairDispatch.Enqueue(repair);
		}
	}
}
