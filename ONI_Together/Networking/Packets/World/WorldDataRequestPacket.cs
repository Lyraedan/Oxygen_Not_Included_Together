using ONI_Together.DebugTools;
using ONI_Together.Misc.World;
using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;
using Utils = ONI_Together.Misc.Utils;

namespace ONI_Together.Networking.Packets.World
{
	public class WorldDataRequestPacket : IPacket
	{
		private const int ChunkSize = 16;
		private static int _rejectedPackets;
		private static readonly Dictionary<ulong, ActiveTransfer> ActiveTransfers = new();
		public ulong SenderId;
		public long SnapshotGeneration;

		private sealed class ActiveTransfer
		{
			internal long Generation;
			internal List<WorldDataPacket> Packets;
			internal WorldDataSendWindow Window;
		}
		internal static bool ShouldAccept(ulong senderId, DispatchContext context) =>
			senderId != 0 && !context.SenderIsHost && SyncBarrier.SenderMatches(senderId, context.SenderId);
		internal static bool IsValidSnapshotGeneration(long snapshotGeneration, bool generationMatches) =>
			snapshotGeneration > 0 && generationMatches;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(SenderId);
			writer.Write(SnapshotGeneration);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			SenderId = reader.ReadUInt64();
			SnapshotGeneration = reader.ReadInt64();
			if (SnapshotGeneration <= 0)
				throw new InvalidDataException("World baseline request requires a positive snapshot generation");
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;
			if (!ShouldAccept(SenderId, PacketHandler.CurrentContext)
				|| !IsValidSnapshotGeneration(
					SnapshotGeneration,
					ReadyManager.IsCurrentSnapshot(SenderId, SnapshotGeneration)))
			{
				int rejected = ++_rejectedPackets;
				if (rejected <= 5 || rejected % 100 == 0)
					DebugConsole.LogWarning($"[WorldDataRequestPacket] Rejected sender {SenderId} from {PacketHandler.CurrentContext.SenderId}, host={PacketHandler.CurrentContext.SenderIsHost} (#{rejected})");
				return;
			}

			// Immediately send full world data back to the requester.
			if (!SendWorldData(SenderId, SnapshotGeneration))
			{
				DebugConsole.LogError(
					$"[WorldDataRequestPacket] Failed world baseline for {SenderId}; aborting sync");
				ReadyManager.AbortSyncBarrier(SenderId);
				if (MultiplayerSession.ConnectedPlayers.TryGetValue(SenderId, out MultiplayerPlayer player))
					player.CompleteSaveTransfer();
				NetworkConfig.TransportServer?.KickClient(SenderId);
			}
		}

		public static bool SendWorldData(ulong target, long snapshotGeneration)
		{
			using var _ = Profiler.Scope();
			if (!MultiplayerSession.IsHost
				|| !ReadyManager.IsCurrentSnapshot(target, snapshotGeneration)
				|| Grid.WidthInCells <= 0
				|| Grid.HeightInCells <= 0
				|| !ReadyManager.TryBeginWorldBaseline(target, snapshotGeneration))
			{
				return false;
			}

			DebugConsole.Log($"[WorldDataRequestPacket] Sending world data to {target}");
			WorldUpdateBatcher.Flush();
			NetworkIdentityRegistry.RetireUnstableElementLifecyclesForSnapshot();
			long worldUpdateForegroundBaseline = WorldUpdatePacket.CurrentHostForegroundSequence;
			long worldUpdateRevisionBaseline = WorldUpdatePacket.CurrentHostRevision;
			long worldUpdateRepairSequenceBaseline =
				WorldUpdatePacket.CurrentHostRepairDispatchSequence;
			var lifecycleBaseline = new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>(
				NetworkIdentityRegistry.GetLifecycleRevisionSnapshot());

			List<ChunkData> chunks = Utils.CollectChunks(
					startX: 0,
					startY: 0,
					chunkSize: ChunkSize,
					numChunksX: ChunkCountForDimension(Grid.WidthInCells),
					numChunksY: ChunkCountForDimension(Grid.HeightInCells)
				);
			List<WorldDataPacket> packets = BuildPackets(
				snapshotGeneration, chunks, lifecycleBaseline,
				worldUpdateForegroundBaseline, worldUpdateRevisionBaseline,
				worldUpdateRepairSequenceBaseline);
			if (packets.Count == 0)
				return false;
			var transfer = new ActiveTransfer
			{
				Generation = snapshotGeneration,
				Packets = packets,
				Window = new WorldDataSendWindow(packets.Count),
			};
			ActiveTransfers[target] = transfer;
			if (!TrySendAvailable(target, transfer))
			{
				ActiveTransfers.Remove(target);
				return false;
			}
			DebugConsole.Log(
				$"[WorldDataRequestPacket] Snapshot {snapshotGeneration} started for {target}: " +
				$"{packets.Count} parts, window={WorldDataSendWindow.MaxInFlightChunks}, " +
				$"maxFragments={WorldDataPacket.MaxInFlightReliableFragments}");
			return true;
		}

		internal static bool AcceptProgress(
			ulong target, long snapshotGeneration, int appliedThroughChunk)
		{
			if (!ActiveTransfers.TryGetValue(target, out ActiveTransfer transfer)
			    || transfer.Generation != snapshotGeneration
			    || !ReadyManager.IsCurrentSnapshot(target, snapshotGeneration)
			    || !transfer.Window.AcceptProgress(appliedThroughChunk))
				return false;
			if (transfer.Window.IsComplete)
			{
				ActiveTransfers.Remove(target);
				DebugConsole.Log(
					$"[WorldDataRequestPacket] Snapshot {snapshotGeneration} fully applied by {target}");
				return true;
			}
			if (TrySendAvailable(target, transfer))
				return true;
			CancelTransfer(target);
			ReadyManager.AbortSyncBarrier(target);
			NetworkConfig.TransportServer?.KickClient(target);
			return false;
		}

		internal static void CancelTransfer(ulong target)
			=> ActiveTransfers.Remove(target);

		internal static void ResetSessionState()
		{
			ActiveTransfers.Clear();
			_rejectedPackets = 0;
			WorldDataPacket.ResetSessionState();
		}

		internal static bool HasActiveTransferForTests(ulong target, long generation)
			=> ActiveTransfers.TryGetValue(target, out ActiveTransfer transfer)
			   && transfer.Generation == generation;

		private static bool TrySendAvailable(ulong target, ActiveTransfer transfer)
			=> transfer.Window.TrySendAvailable(index => PacketSender.SendToPlayer(
				target, transfer.Packets[index], PacketSendMode.Reliable));

		internal static List<WorldDataPacket> BuildPacketsForTests(
			long generation,
			IReadOnlyList<ChunkData> chunks,
			List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry> lifecycle,
			long foregroundCut,
			long revisionCut,
			long repairCut)
			=> BuildPackets(
				generation, chunks, lifecycle, foregroundCut, revisionCut, repairCut);

		private static List<WorldDataPacket> BuildPackets(
			long generation,
			IReadOnlyList<ChunkData> chunks,
			List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry> lifecycle,
			long foregroundCut,
			long revisionCut,
			long repairCut)
		{
			if (!HasValidTransferInput(
				    generation, chunks, lifecycle, foregroundCut, revisionCut, repairCut))
				return new List<WorldDataPacket>();
			int lifecyclePages = (lifecycle.Count + WorldDataPacket.MaxLifecycleEntriesPerPacket - 1)
			                     / WorldDataPacket.MaxLifecycleEntriesPerPacket;
			int partCount = System.Math.Max(chunks.Count, lifecyclePages);
			if (partCount <= 0 || partCount > WorldDataPacket.MaxChunkCount)
				return new List<WorldDataPacket>();

			var packets = new List<WorldDataPacket>(partCount);
			for (int index = 0; index < partCount; index++)
			{
				bool final = index == partCount - 1;
				packets.Add(new WorldDataPacket
				{
					SnapshotGeneration = generation,
					IsFinalChunk = final,
					ChunkIndex = index,
					ChunkCount = partCount,
					GridChunkCount = chunks.Count,
					LifecycleBaselineTotalEntries = lifecycle.Count,
					Chunks = index < chunks.Count
						? new List<ChunkData> { chunks[index] }
						: new List<ChunkData>(),
					WorldUpdateForegroundBaseline = final ? foregroundCut : 0,
					WorldUpdateRevisionBaseline = final ? revisionCut : 0,
					WorldUpdateRepairSequenceBaseline = final ? repairCut : 0,
					LifecycleBaseline = CreateLifecyclePage(lifecycle, index),
				});
			}
			return packets;
		}

		private static List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>
			CreateLifecyclePage(
				List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry> lifecycle,
				int pageIndex)
		{
			int start = pageIndex * WorldDataPacket.MaxLifecycleEntriesPerPacket;
			if (start >= lifecycle.Count)
				return new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>();
			int count = System.Math.Min(
				WorldDataPacket.MaxLifecycleEntriesPerPacket, lifecycle.Count - start);
			return lifecycle.GetRange(start, count);
		}

		private static bool HasValidTransferInput(
			long generation,
			IReadOnlyList<ChunkData> chunks,
			IReadOnlyList<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry> lifecycle,
			long foregroundCut,
			long revisionCut,
			long repairCut)
		{
			if (generation <= 0 || chunks == null || chunks.Count <= 0
			    || chunks.Count > WorldDataPacket.MaxChunkCount || lifecycle == null
			    || lifecycle.Count > WorldDataPacket.MaxLifecycleBaselineEntries
			    || foregroundCut < 0 || revisionCut < 0 || repairCut < 0)
				return false;
			var netIds = new HashSet<int>();
			foreach (NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry entry in lifecycle)
			{
				if (!netIds.Add(entry.NetId)
				    || !WorldLifecycleBaselineCodec.IsValidTransferEntry(entry))
					return false;
			}
			return true;
		}

		internal static int ChunkCountForDimension(int cells) =>
			cells <= 0 ? 0 : (cells + ChunkSize - 1) / ChunkSize;
	}
}
