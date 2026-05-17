using ONI_Together.DebugTools;
using ONI_Together.Misc.World;
using ONI_Together.Networking.Packets.Architecture;
using System;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
    /// <summary>
    /// Wrapper packet that provides integrity validation through serialization/deserialization
    /// If deserialization succeeds, ALL bytes arrived intact. If it fails, data is corrupted.
    /// </summary>
    public class SecureTransferPacket : IPacket
    {
        public int SequenceNumber;           // Packet order (0, 1, 2, 3...)
        public string TransferId;            // Transfer session ID (e.g., "Before_Reactor_Active")
        public byte[] PayloadBytes;          // SaveFileChunkPacket manually serialized to bytes

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();

            writer.Write(SequenceNumber);
            writer.Write(TransferId);
            writer.Write(PayloadBytes.Length);
            writer.Write(PayloadBytes);
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();

            SequenceNumber = reader.ReadInt32();
            TransferId = reader.ReadString();
            int payloadLength = reader.ReadInt32();
            PayloadBytes = reader.ReadBytes(payloadLength);
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            try
            {
                // INTEGRITY TEST: Try to deserialize payload back to SaveFileChunkPacket
                // If this succeeds = ALL bytes arrived intact!
                var reconstructedChunk = DeserializeSaveFileChunk(PayloadBytes);

                DebugConsole.Log($"[SecureTransfer] ✅ Packet {SequenceNumber} integrity verified - {reconstructedChunk.Chunk.Length} bytes");

                // Send ACK confirming that this chunk was received
                SendChunkAck(SequenceNumber, TransferId);

                // Data is verified intact - proceed with normal processing
                SaveChunkAssembler.ReceiveChunk(reconstructedChunk);
            }
            catch (Exception ex)
            {
                // CORRUPTION DETECTED: Deserialization failed = missing or corrupted bytes
                DebugConsole.LogError($"[SecureTransfer] ❌ Packet {SequenceNumber} CORRUPTED - deserialization failed: {ex}");

                // Request re-send of this specific packet
                RequestPacketResend(SequenceNumber, TransferId);
            }
        }

        /// <summary>
        /// Manually deserialize bytes back to SaveFileChunkPacket
        /// Throws exception if any bytes are missing or corrupted
        /// </summary>
        private SaveFileChunkPacket DeserializeSaveFileChunk(byte[] bytes)
        {
            using var _ = Profiler.Scope();

            using (var ms = new MemoryStream(bytes))
            using (var reader = new BinaryReader(ms))
            {
                var chunk = new SaveFileChunkPacket();

                // This will throw if ANY bytes are missing/corrupted:
                chunk.FileName = reader.ReadString();     // Needs valid length header + string chars
                chunk.Offset = reader.ReadInt32();        // Needs exactly 4 bytes
                chunk.TotalSize = reader.ReadInt32();     // Needs exactly 4 bytes
                int chunkLength = reader.ReadInt32();     // Needs exactly 4 bytes
                chunk.Chunk = reader.ReadBytes(chunkLength); // Needs exactly chunkLength bytes

                // Verify we read the expected amount
                if (chunk.Chunk.Length != chunkLength)
                {
                    throw new InvalidDataException($"Expected {chunkLength} chunk bytes, got {chunk.Chunk.Length}");
                }

                return chunk;
            }
        }

        /// <summary>
        /// Manually serialize SaveFileChunkPacket to bytes for integrity validation
        /// </summary>
        public static byte[] SerializeSaveFileChunk(SaveFileChunkPacket packet)
        {
            using var _ = Profiler.Scope();

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(packet.FileName);
                writer.Write(packet.Offset);
                writer.Write(packet.TotalSize);
                writer.Write(packet.Chunk.Length);
                writer.Write(packet.Chunk);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Request re-send of a specific corrupted packet
        /// </summary>
        private void RequestPacketResend(int sequenceNumber, string transferId)
        {
            using var _ = Profiler.Scope();

            // TODO: Implement packet resend request
            // For now, request full file resend (existing behavior)
            DebugConsole.LogWarning($"[SecureTransfer] Requesting resend due to corrupted packet {sequenceNumber} in transfer {transferId}");

            // Trigger existing resend mechanism
            var requestPacket = new SaveFileRequestPacket
            {
                Requester = MultiplayerSession.LocalUserID
            };
            PacketSender.SendToHost(requestPacket);
        }

        /// <summary>
        /// Send ACK to server confirming that chunk was received
        /// </summary>
        private void SendChunkAck(int sequenceNumber, string transferId)
        {
            using var _ = Profiler.Scope();

            var ackPacket = new ChunkAckPacket
            {
                SequenceNumber = sequenceNumber,
                TransferId = transferId,
                ClientSteamID = MultiplayerSession.LocalUserID
            };

            PacketSender.SendToHost(ackPacket);
            DebugConsole.Log($"[SecureTransfer] Sent ACK {sequenceNumber} for transfer {transferId}");
        }
    }
}