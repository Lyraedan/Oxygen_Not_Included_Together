using ONI_Together.Networking.Packets.Architecture;
using System;
using System.Collections.Generic;
using System.IO;

namespace ONI_Together.Networking.Packets.Core
{
    internal class ChunkedPacket : IPacket
    {
		internal const int SerializedEnvelopeBytes = sizeof(int) * 5;
        public const int MaxChunkDataBytes = 512 * 1024;
        public const int MaxChunks = 32768;
        private const int MaxPendingAssemblies = 64;
        private const int MaxPendingBytes = PacketHandler.MaxPacketSize * 2;
		private const int MaxCompletedSequences = 1024;
        private static readonly TimeSpan PendingTimeout = TimeSpan.FromSeconds(30);

        public int SequenceId;
        public int ChunkIndex;
        public int TotalChunks;
        public byte[] ChunkData = Array.Empty<byte>();

        private sealed class PendingAssembly
        {
            public readonly byte[][] Chunks;
            public readonly DispatchContext Context;
            public int ReceivedCount;
            public int TotalBytes;
            public System.DateTime LastUpdatedUtc;

            public PendingAssembly(int totalChunks, DispatchContext context, System.DateTime now)
            {
                Chunks = new byte[totalChunks][];
                Context = context;
                LastUpdatedUtc = now;
            }
        }

		private static readonly Dictionary<
			(ulong SenderId, long Generation, long SessionEpoch, int SequenceId),
			PendingAssembly> PendingChunks = new();
		private static readonly Dictionary<
			(ulong SenderId, long Generation, long SessionEpoch, int SequenceId),
			System.DateTime> CompletedSequences = new();
		private static readonly Queue<
			(ulong SenderId, long Generation, long SessionEpoch, int SequenceId)>
			CompletedOrder = new();
        private static int _nextSequenceId;
        private static int _pendingBytes;

		public static void ResetSessionState()
		{
			PendingChunks.Clear();
			CompletedSequences.Clear();
			CompletedOrder.Clear();
			_nextSequenceId = 0;
			_pendingBytes = 0;
		}

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(SequenceId);
            writer.Write(ChunkIndex);
            writer.Write(TotalChunks);
            writer.Write(ChunkData.Length);
            writer.Write(ChunkData);
        }

        public void Deserialize(BinaryReader reader)
        {
            SequenceId = reader.ReadInt32();
            ChunkIndex = reader.ReadInt32();
            TotalChunks = reader.ReadInt32();
            int length = reader.ReadInt32();
            ValidateHeader(TotalChunks, ChunkIndex, length);
            if (reader.BaseStream.CanSeek && reader.BaseStream.Length - reader.BaseStream.Position < length)
                throw new EndOfStreamException("Chunk payload is truncated");
            ChunkData = reader.ReadBytes(length);
            if (ChunkData.Length != length)
                throw new EndOfStreamException("Chunk payload is truncated");
        }

		public void OnDispatched()
		{
			DispatchContext context = PacketHandler.CurrentContext;
			if (TryAcceptChunk(this, context, out byte[] fullData, out DispatchContext completedContext))
			{
				if (!PacketHandler.TryHandleIncoming(fullData, completedContext.AsChunkReassembled()))
					throw new InvalidDataException("Reassembled chunk payload was rejected");
			}
		}

        internal static bool TryAcceptChunk(
            ChunkedPacket packet,
            DispatchContext context,
            out byte[] fullData,
            out DispatchContext completedContext)
        {
            System.DateTime now = System.DateTime.UtcNow;
            CleanupExpired(now);
            ValidateHeader(packet.TotalChunks, packet.ChunkIndex, packet.ChunkData?.Length ?? -1);

			var key = (
				context.SenderId,
				context.ConnectionGeneration,
				context.SessionEpoch,
				packet.SequenceId);
			if (CompletedSequences.ContainsKey(key))
			{
				fullData = Array.Empty<byte>();
				completedContext = default;
				return false;
			}
            PendingAssembly pending = GetOrCreatePending(key, packet, context, now);
            pending.LastUpdatedUtc = now;
            AddChunk(key, pending, packet);

            if (pending.ReceivedCount != pending.Chunks.Length)
            {
                fullData = Array.Empty<byte>();
                completedContext = default;
                return false;
            }

            fullData = Assemble(pending);
            completedContext = pending.Context;
            RemovePending(key);
			AddCompleted(key, now);
            return true;
        }

        public static int GetNextSequenceId()
        {
            return _nextSequenceId++;
        }

		internal static bool TrySendSerializedChunks(
			byte[] fullData,
			int exclusiveWireLimit,
			Func<ChunkedPacket, bool> sendChunk)
		{
			if (fullData == null || fullData.Length == 0
			    || fullData.Length > PacketHandler.MaxPacketSize || sendChunk == null)
				return false;
			int chunkDataBytes = Math.Min(
				MaxChunkDataBytes,
				exclusiveWireLimit - SerializedEnvelopeBytes - 1);
			if (chunkDataBytes <= 0)
				return false;
			int totalChunks = (fullData.Length - 1) / chunkDataBytes + 1;
			if (totalChunks > MaxChunks)
				return false;
			int sequenceId = GetNextSequenceId();
			for (int index = 0; index < totalChunks; index++)
			{
				int offset = index * chunkDataBytes;
				int length = Math.Min(chunkDataBytes, fullData.Length - offset);
				var data = new byte[length];
				Array.Copy(fullData, offset, data, 0, length);
				if (!sendChunk(new ChunkedPacket
				    {
					    SequenceId = sequenceId,
					    ChunkIndex = index,
					    TotalChunks = totalChunks,
					    ChunkData = data,
				    }))
					return false;
			}
			return true;
		}

        private static void ValidateHeader(int totalChunks, int chunkIndex, int length)
        {
            if (totalChunks <= 0 || totalChunks > MaxChunks)
                throw new InvalidDataException($"Invalid total chunk count: {totalChunks}");
            if (chunkIndex < 0 || chunkIndex >= totalChunks)
                throw new InvalidDataException($"Invalid chunk index: {chunkIndex}");
            if (length < 0 || length > MaxChunkDataBytes)
                throw new InvalidDataException($"Invalid chunk length: {length}");
        }

        private static PendingAssembly GetOrCreatePending(
			(ulong SenderId, long Generation, long SessionEpoch, int SequenceId) key,
            ChunkedPacket packet,
            DispatchContext context,
            System.DateTime now)
        {
            if (!PendingChunks.TryGetValue(key, out PendingAssembly pending))
            {
                if (PendingChunks.Count >= MaxPendingAssemblies)
                    throw new InvalidDataException("Too many pending chunk assemblies");
                pending = new PendingAssembly(packet.TotalChunks, context, now);
                PendingChunks[key] = pending;
                return pending;
            }
            if (pending.Chunks.Length == packet.TotalChunks && pending.Context.SenderIsHost == context.SenderIsHost)
                return pending;

            RemovePending(key);
            throw new InvalidDataException("Chunk sequence metadata changed during assembly");
        }

        private static void AddChunk(
			(ulong SenderId, long Generation, long SessionEpoch, int SequenceId) key,
            PendingAssembly pending,
            ChunkedPacket packet)
        {
            if (pending.Chunks[packet.ChunkIndex] != null)
                return;
            int newAssemblyBytes = checked(pending.TotalBytes + packet.ChunkData.Length);
            if (newAssemblyBytes > PacketHandler.MaxPacketSize || _pendingBytes > MaxPendingBytes - packet.ChunkData.Length)
            {
                RemovePending(key);
                throw new InvalidDataException("Chunk assembly exceeds maximum packet size");
            }
            pending.TotalBytes = newAssemblyBytes;
            pending.Chunks[packet.ChunkIndex] = packet.ChunkData;
            pending.ReceivedCount++;
            _pendingBytes += packet.ChunkData.Length;
        }

        private static byte[] Assemble(PendingAssembly pending)
        {
            byte[] data = new byte[pending.TotalBytes];
            int offset = 0;
            foreach (byte[] chunk in pending.Chunks)
            {
                Array.Copy(chunk, 0, data, offset, chunk.Length);
                offset += chunk.Length;
            }
            return data;
        }

		private static void RemovePending(
			(ulong SenderId, long Generation, long SessionEpoch, int SequenceId) key)
        {
            if (!PendingChunks.TryGetValue(key, out PendingAssembly pending))
                return;
            _pendingBytes -= pending.TotalBytes;
            PendingChunks.Remove(key);
        }

        private static void CleanupExpired(System.DateTime now)
        {
			List<(ulong SenderId, long Generation, long SessionEpoch, int SequenceId)>
				expired = null;
            foreach (var entry in PendingChunks)
            {
                if (now - entry.Value.LastUpdatedUtc <= PendingTimeout)
                    continue;
				expired ??= new List<
					(ulong SenderId, long Generation, long SessionEpoch, int SequenceId)>();
                expired.Add(entry.Key);
            }
			if (expired != null)
			{
				foreach (var key in expired)
					RemovePending(key);
			}
			while (CompletedOrder.Count > 0)
			{
				var key = CompletedOrder.Peek();
				if (CompletedSequences.TryGetValue(key, out System.DateTime completedAt)
				    && now - completedAt <= PendingTimeout)
					break;
				CompletedOrder.Dequeue();
				CompletedSequences.Remove(key);
			}
        }

		private static void AddCompleted(
			(ulong SenderId, long Generation, long SessionEpoch, int SequenceId) key,
			System.DateTime now)
		{
			CompletedSequences[key] = now;
			CompletedOrder.Enqueue(key);
			while (CompletedOrder.Count > MaxCompletedSequences)
				CompletedSequences.Remove(CompletedOrder.Dequeue());
		}
    }
}
