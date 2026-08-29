using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;

namespace ONI_Together.Networking.Packets.Core
{
	internal class ChunkedPacket : IPacket
	{
		public int SequenceId;
		public int ChunkIndex;
		public int TotalChunks;
		public byte[] ChunkData;

		// A 1000 byte budget over a 512 KB save chunk is ~530 pieces; anything past this is a
		// corrupt header rather than a real message.
		private const int MAX_CHUNKS_PER_MESSAGE = 4096;

		private const float PENDING_CHUNK_TIMEOUT_SECONDS = 30f;

		private static Dictionary<int, byte[][]> _pendingChunks = new Dictionary<int, byte[][]>();
		private static Dictionary<int, float> _pendingStartedAt = new Dictionary<int, float>();
		private static List<int> _stalePending = new List<int>();

		// NOTE: the pending map is keyed on SequenceId alone, and every peer runs its own
		// counter from 0 - so on a host receiving chunked messages from two clients at once,
		// their sequence ids collide and the two payloads corrupt each other. Fixing it needs
		// a sender id on the wire (PacketHandler.HandleIncoming takes only bytes and has no
		// notion of who sent them), which is a protocol change and out of scope here.
		private static int _nextSequenceId = 0;

		public ChunkedPacket() { }

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
			int len = reader.ReadInt32();
			ChunkData = reader.ReadBytes(len);
		}

		public void OnDispatched()
		{
			if (TryReassemble(SequenceId, ChunkIndex, TotalChunks, ChunkData, out byte[] fullData))
				PacketHandler.HandleIncoming(fullData);
		}

		/// <summary>
		/// Accumulate one chunk and hand back the original payload once the set is complete.
		/// Separated from <see cref="OnDispatched"/> so the reassembly can be exercised without
		/// dispatching a packet - a test that reimplements the join would only be testing its
		/// own copy of it.
		/// </summary>
		internal static bool TryReassemble(int sequenceId, int chunkIndex, int totalChunks, byte[] chunkData, out byte[] fullData)
		{
			fullData = null;

			// The header arrives off the wire, so treat it as hostile: a corrupt or malicious
			// TotalChunks would otherwise size an allocation, and a ChunkIndex outside it would
			// throw out of a packet handler.
			if (totalChunks <= 0 || totalChunks > MAX_CHUNKS_PER_MESSAGE)
				return false;

			if (chunkIndex < 0 || chunkIndex >= totalChunks || chunkData == null)
				return false;

			DropStalePending();

			if (!_pendingChunks.TryGetValue(sequenceId, out var chunks) || chunks.Length != totalChunks)
			{
				chunks = new byte[totalChunks][];
				_pendingChunks[sequenceId] = chunks;
			}

			chunks[chunkIndex] = chunkData;
			_pendingStartedAt[sequenceId] = UnityEngine.Time.unscaledTime;

			int totalSize = 0;
			for (int i = 0; i < totalChunks; i++)
			{
				if (chunks[i] == null)
					return false;

				totalSize += chunks[i].Length;
			}

			_pendingChunks.Remove(sequenceId);
			_pendingStartedAt.Remove(sequenceId);

			fullData = new byte[totalSize];
			int offset = 0;
			foreach (var chunk in chunks)
			{
				System.Array.Copy(chunk, 0, fullData, offset, chunk.Length);
				offset += chunk.Length;
			}

			return true;
		}

		/// <summary>
		/// Chunked sends inherit the delivery method of the message they carry, so an
		/// unreliable batch that loses one chunk never completes. Without this the partial
		/// set stays in the map for the rest of the session.
		/// </summary>
		private static void DropStalePending()
		{
			if (_pendingStartedAt.Count == 0)
				return;

			float now = UnityEngine.Time.unscaledTime;
			_stalePending.Clear();

			foreach (var kvp in _pendingStartedAt)
			{
				if (now - kvp.Value > PENDING_CHUNK_TIMEOUT_SECONDS)
					_stalePending.Add(kvp.Key);
			}

			foreach (int sequenceId in _stalePending)
			{
				_pendingChunks.Remove(sequenceId);
				_pendingStartedAt.Remove(sequenceId);
			}
		}

		/// <summary>Drop all partial reassemblies. For tests and for session teardown.</summary>
		internal static void ResetPending()
		{
			_pendingChunks.Clear();
			_pendingStartedAt.Clear();
		}

		public static int GetNextSequenceId()
		{
			return _nextSequenceId++;
		}
	}
}
