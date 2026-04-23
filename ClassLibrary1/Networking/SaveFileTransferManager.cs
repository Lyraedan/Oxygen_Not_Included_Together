using ONI_MP.DebugTools;
using ONI_MP.Networking.Packets.World;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Networking
{
    /// <summary>
    /// Manages transfers with ACK system
    /// Tracks sent chunks and received ACKs to detect losses and resend specific chunks
    /// </summary>
    public static class SaveFileTransferManager
    {
        private class ClientTransfer
        {
            public ulong ClientID;
            public string TransferId;
            public string FileName;
            public byte[] FileData;
            public int TotalChunks;
            public int ChunkSize;

            public bool[] ChunkSent;           // [true, true, false] - which were sent
            public bool[] ChunkAcked;          // [true, false, false] - which were confirmed
            public System.DateTime[] ChunkSentTime;   // When each chunk was sent

            public int HighestAckReceived = -1; // Last sequential ACK received
            public System.DateTime LastActivity = System.DateTime.Now;

            public ClientTransfer(ulong clientID, string transferId, string fileName, byte[] data, int chunkSize)
            {
                using var _ = Profiler.Scope();

                ClientID = clientID;
                TransferId = transferId;
                FileName = fileName;
                FileData = data;
                ChunkSize = chunkSize;
                TotalChunks = (int)Math.Ceiling((double)data.Length / chunkSize);

                ChunkSent = new bool[TotalChunks];
                ChunkAcked = new bool[TotalChunks];
                ChunkSentTime = new System.DateTime[TotalChunks];
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
        public static void StartTransfer(ulong clientID, string transferId, string fileName, byte[] data, int chunkSize)
        {
            using var _ = Profiler.Scope();

            string key = GetTransferKey(clientID, transferId);
            var transfer = new ClientTransfer(clientID, transferId, fileName, data, chunkSize);
            ActiveTransfers[key] = transfer;

            DebugConsole.Log($"[TransferManager] Started transfer {transferId} to {clientID} - {transfer.TotalChunks} chunks");
        }

        /// <summary>
        /// Mark chunk as sent when server sends it
        /// </summary>
        public static void MarkChunkSent(ulong clientID, string transferId, int chunkIndex)
        {
            using var _ = Profiler.Scope();

            string key = GetTransferKey(clientID, transferId);
            if (ActiveTransfers.TryGetValue(key, out var transfer))
            {
                transfer.ChunkSent[chunkIndex] = true;
                transfer.ChunkSentTime[chunkIndex] = System.DateTime.Now;
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

            if (chunkIndex >= 0 && chunkIndex < transfer.TotalChunks)
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
        /// Detect lost chunks and resend only the necessary ones
        /// </summary>
        public static void CheckForLostChunks()
        {
            using var _ = Profiler.Scope();

            var now = System.DateTime.Now;

            foreach (var transfer in ActiveTransfers.Values.ToArray())
            {
                // Check chunks that were sent but haven't received ACK for more than 5 seconds
                for (int i = 0; i <= Math.Min(transfer.HighestAckReceived + 10, transfer.TotalChunks - 1); i++) // Only check window near last ACK
                {
                    if (transfer.ChunkSent[i] && !transfer.ChunkAcked[i] &&
                        (now - transfer.ChunkSentTime[i]).TotalSeconds > 5.0)
                    {
                        DebugConsole.LogWarning($"[TransferManager] Chunk {i} lost for {transfer.ClientID} - resending specific chunk");
                        ResendSpecificChunk(transfer, i);
                    }
                }

                // Remove old transfers
                if ((now - transfer.LastActivity).TotalMinutes > 2)
                {
                    DebugConsole.LogWarning($"[TransferManager] Transfer {transfer.TransferId} to {transfer.ClientID} timed out");
                    ActiveTransfers.Remove(GetTransferKey(transfer.ClientID, transfer.TransferId));
                }
            }
        }

        /// <summary>
        /// Resend specific lost chunk
        /// </summary>
        private static void ResendSpecificChunk(ClientTransfer transfer, int chunkIndex)
        {
            using var _ = Profiler.Scope();

            try
            {
                int offset = chunkIndex * transfer.ChunkSize;
                int size = Math.Min(transfer.ChunkSize, transfer.FileData.Length - offset);
                byte[] chunkData = new byte[size];
                Buffer.BlockCopy(transfer.FileData, offset, chunkData, 0, size);

                var chunkPacket = new SaveFileChunkPacket
                {
                    FileName = transfer.FileName,
                    Offset = offset,
                    TotalSize = transfer.FileData.Length,
                    Chunk = chunkData
                };

                var securePacket = new SecureTransferPacket
                {
                    SequenceNumber = chunkIndex,
                    TransferId = transfer.TransferId,
                    PayloadBytes = SecureTransferPacket.SerializeSaveFileChunk(chunkPacket)
                };

                PacketSender.SendToPlayer(transfer.ClientID, securePacket);

                // Update send timestamp
                transfer.ChunkSentTime[chunkIndex] = System.DateTime.Now;
                transfer.LastActivity = System.DateTime.Now;

                DebugConsole.Log($"[TransferManager] Resent chunk {chunkIndex} to {transfer.ClientID}");
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[TransferManager] Failed to resend chunk {chunkIndex}: {ex.Message}");
            }
        }
    }
}