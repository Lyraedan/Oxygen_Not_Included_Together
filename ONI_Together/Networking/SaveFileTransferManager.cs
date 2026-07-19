using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.World;
using System;
using System.Collections.Generic;
using System.Linq;
using Shared.Profiling;

namespace ONI_Together.Networking
{
    /// <summary>
    /// Tracks application-level ACKs for reliable save transfers.
    /// </summary>
    public static class SaveFileTransferManager
    {
        internal const int AckWindowChunks = 4;
		internal const int MaxTrackedChunks =
			(SaveFileChunkPacket.MaxSaveBytes + SaveFileChunkPacket.MaxChunkBytes - 1)
			/ SaveFileChunkPacket.MaxChunkBytes;

        internal enum ChunkSendDecision
        {
            Send,
            Wait,
            Stop
        }

        private class ClientTransfer
        {
            public ulong ClientID;
            public string TransferId;
            public int TotalChunks;

            public bool[] ChunkAcked;          // [true, false, false] - which were confirmed

            public int HighestChunkSent = -1;
            public int HighestAckReceived = -1; // Last sequential ACK received
            public System.DateTime LastActivity = System.DateTime.Now;

			public ClientTransfer(ulong clientID, string transferId, int totalChunks)
            {
                using var _ = Profiler.Scope();

                ClientID = clientID;
                TransferId = transferId;
				TotalChunks = totalChunks;
                ChunkAcked = new bool[TotalChunks];
            }
        }

        private static readonly Dictionary<string, ClientTransfer> ActiveTransfers = new Dictionary<string, ClientTransfer>();

        private static string GetTransferKey(ulong clientID, string transferId)
        {
            using var _ = Profiler.Scope();

            return $"{clientID}_{transferId}";
        }

        /// <summary>
        /// Register new transfer and track chunks
        /// </summary>
		public static void StartTransfer(
			ulong clientID,
			string transferId,
			int totalChunks)
        {
            using var _ = Profiler.Scope();
			if (clientID == 0)
				throw new ArgumentException("Save transfer client ID must be non-zero", nameof(clientID));
			if (string.IsNullOrEmpty(transferId)
			    || transferId.Length > SecureTransferPacket.MaxTransferIdChars)
				throw new ArgumentException("Invalid save transfer ID", nameof(transferId));
			if (totalChunks <= 0 || totalChunks > MaxTrackedChunks)
				throw new ArgumentOutOfRangeException(nameof(totalChunks), totalChunks,
					$"Save transfer chunk count must be between 1 and {MaxTrackedChunks}");

            string key = GetTransferKey(clientID, transferId);
			var transfer = new ClientTransfer(clientID, transferId, totalChunks);
            ActiveTransfers[key] = transfer;

            DebugConsole.Log($"[TransferManager] Started transfer {transferId} to {clientID} - {transfer.TotalChunks} chunks");
		}

		public static void CancelTransfers(ulong clientID)
		{
			foreach (string key in ActiveTransfers
			         .Where(entry => entry.Value.ClientID == clientID)
			         .Select(entry => entry.Key)
			         .ToArray())
			{
				ActiveTransfers.Remove(key);
			}
		}

		internal static ChunkSendDecision GetChunkSendDecision(
			ulong clientID,
			string transferId,
			int chunkIndex)
        {
            if (!ActiveTransfers.TryGetValue(GetTransferKey(clientID, transferId), out var transfer)
                || chunkIndex < 0
                || chunkIndex >= transfer.TotalChunks
                || chunkIndex != transfer.HighestChunkSent + 1)
            {
                return ChunkSendDecision.Stop;
            }

            int chunksAheadOfAck = chunkIndex - (transfer.HighestAckReceived + 1);
            return chunksAheadOfAck < AckWindowChunks
				? ChunkSendDecision.Send
				: ChunkSendDecision.Wait;
		}

		internal static void ResetSessionState()
		{
			ActiveTransfers.Clear();
		}

        /// <summary>
        /// Mark chunk as sent when server sends it
        /// </summary>
        public static void MarkChunkSent(ulong clientID, string transferId, int chunkIndex)
        {
            using var _ = Profiler.Scope();

            string key = GetTransferKey(clientID, transferId);
            if (ActiveTransfers.TryGetValue(key, out var transfer)
                && chunkIndex == transfer.HighestChunkSent + 1
                && chunkIndex < transfer.TotalChunks)
            {
                transfer.HighestChunkSent = chunkIndex;
                transfer.LastActivity = System.DateTime.Now;
            }
        }

        /// <summary>
        /// Process ACK received from client
        /// </summary>
        public static void HandleChunkAck(ulong clientID, string transferId, int chunkIndex)
        {
            using var _ = Profiler.Scope();

            string key = GetTransferKey(clientID, transferId);
            if (!ActiveTransfers.TryGetValue(key, out var transfer))
            {
                DebugConsole.LogWarning($"[TransferManager] Received ACK {chunkIndex} for unknown transfer {transferId} from {clientID}");
                return;
            }

            if (chunkIndex >= 0
                && chunkIndex <= transfer.HighestChunkSent
                && !transfer.ChunkAcked[chunkIndex])
            {
                transfer.ChunkAcked[chunkIndex] = true;
                transfer.LastActivity = System.DateTime.Now;

                // TCP-like ACK processing - tolerant to out-of-order ACKs
                DebugConsole.Log($"[TransferManager] ACK {chunkIndex} received (highest sequential before: {transfer.HighestAckReceived})");

                // Always tries to advance HighestAckReceived based on confirmed contiguous chunks
                while (transfer.HighestAckReceived + 1 < transfer.TotalChunks &&
                       transfer.ChunkAcked[transfer.HighestAckReceived + 1])
                {
                    transfer.HighestAckReceived++;
                }

                DebugConsole.Log($"[TransferManager] Updated highest sequential ACK: {transfer.HighestAckReceived}/{transfer.TotalChunks}");

                // Check if transfer is complete
                if (transfer.HighestAckReceived + 1 == transfer.TotalChunks)
                {
                    DebugConsole.Log($"[TransferManager] ✅ Transfer {transferId} to {clientID} COMPLETE - all chunks ACKed");
                    ActiveTransfers.Remove(key);
                }
            }
        }

        /// <summary>
        /// Expire transfers that have stopped making application-level progress.
        /// </summary>
        public static void CheckForLostChunks()
        {
            using var _ = Profiler.Scope();

            var now = System.DateTime.Now;

            foreach (var transfer in ActiveTransfers.Values.ToArray())
            {
                if ((now - transfer.LastActivity).TotalMinutes > 2)
                {
                    DebugConsole.LogWarning($"[TransferManager] Transfer {transfer.TransferId} to {transfer.ClientID} timed out");
                    ActiveTransfers.Remove(GetTransferKey(transfer.ClientID, transfer.TransferId));
                }
            }
        }
    }
}
