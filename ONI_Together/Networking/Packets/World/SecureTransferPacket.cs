using ONI_Together.DebugTools;
using ONI_Together.Misc.World;
using ONI_Together.Networking.Packets.Architecture;
using System;
using System.IO;
using Shared.Profiling;
using Shared.Interfaces.Networking;
using System.Security.Cryptography;

namespace ONI_Together.Networking.Packets.World
{
    /// <summary>
    /// Wrapper packet that provides integrity validation through serialization/deserialization
    /// If deserialization succeeds, ALL bytes arrived intact. If it fails, data is corrupted.
    /// </summary>
    public class SecureTransferPacket : IPacket, IHostOnlyPacket
    {
        internal const int MaxTransferIdChars = 64;
        public int SequenceNumber;           // Packet order (0, 1, 2, 3...)
        public string TransferId;            // Transfer session ID (e.g., "Before_Reactor_Active")
        public byte[] PayloadBytes;          // SaveFileChunkPacket manually serialized to bytes

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();
			if (SequenceNumber < 0 || string.IsNullOrEmpty(TransferId)
			    || TransferId.Length > MaxTransferIdChars || PayloadBytes == null
			    || PayloadBytes.Length <= 0 || PayloadBytes.Length > PacketHandler.MaxPacketSize)
				throw new InvalidDataException("Invalid secure transfer packet");

            writer.Write(SequenceNumber);
            writer.Write(TransferId);
            writer.Write(PayloadBytes.Length);
            writer.Write(PayloadBytes);
            writer.Write(ComputePayloadHash(SequenceNumber, TransferId, PayloadBytes));
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();

            SequenceNumber = reader.ReadInt32();
            TransferId = reader.ReadString();
            if (SequenceNumber < 0 || string.IsNullOrEmpty(TransferId) || TransferId.Length > MaxTransferIdChars)
                throw new InvalidDataException("Invalid secure transfer identity");
            int payloadLength = reader.ReadInt32();
            if (payloadLength <= 0 || payloadLength > PacketHandler.MaxPacketSize)
                throw new InvalidDataException($"Invalid secure transfer payload length: {payloadLength}");
            PayloadBytes = reader.ReadBytes(payloadLength);
            if (PayloadBytes.Length != payloadLength)
                throw new EndOfStreamException("Secure transfer payload is truncated");
            byte[] expectedHash = reader.ReadBytes(32);
            if (expectedHash.Length != 32 || !HashesEqual(expectedHash, ComputePayloadHash(SequenceNumber, TransferId, PayloadBytes)))
                throw new InvalidDataException("Secure transfer payload hash mismatch");
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (MultiplayerSession.IsHost || !PacketHandler.CurrentContext.SenderIsHost)
                return;

            try
            {
                // INTEGRITY TEST: Try to deserialize payload back to SaveFileChunkPacket
                // If this succeeds = ALL bytes arrived intact!
                var reconstructedChunk = DeserializeSaveFileChunk(PayloadBytes);

                if (reconstructedChunk.Offset / reconstructedChunk.ChunkSize != SequenceNumber)
                    throw new InvalidDataException("Secure transfer sequence does not match chunk offset");

				if (!SaveChunkAssembler.ReceiveChunk(TransferId, SequenceNumber, reconstructedChunk))
					throw new InvalidDataException("Save chunk metadata was rejected");

				DebugConsole.Log($"[SecureTransfer] Packet {SequenceNumber} verified - {reconstructedChunk.Chunk.Length} bytes");
				SendChunkAck(SequenceNumber, TransferId);
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
        private static SaveFileChunkPacket DeserializeSaveFileChunk(byte[] bytes)
        {
            using var _ = Profiler.Scope();

            using (var ms = new MemoryStream(bytes))
            using (var reader = new BinaryReader(ms))
            {
				var chunk = new SaveFileChunkPacket();
				chunk.Deserialize(reader);
				if (reader.BaseStream.Position != reader.BaseStream.Length)
					throw new InvalidDataException("Save chunk payload contains trailing bytes");
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
				packet.Serialize(writer);
                return ms.ToArray();
            }
        }

		internal static byte[] ComputePayloadHash(int sequenceNumber, string transferId, byte[] payload)
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
			{
				writer.Write(sequenceNumber);
				writer.Write(transferId ?? string.Empty);
				writer.Write(payload?.Length ?? 0);
				if (payload != null)
					writer.Write(payload);
			}
			using SHA256 sha = SHA256.Create();
			return sha.ComputeHash(stream.ToArray());
		}

		private static bool HashesEqual(byte[] left, byte[] right)
		{
			if (left == null || right == null || left.Length != right.Length)
				return false;
			int difference = 0;
			for (int i = 0; i < left.Length; i++)
				difference |= left[i] ^ right[i];
			return difference == 0;
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
	            SaveFileRequestPacket requestPacket = SaveFileRequestPacket.CreateRestart(
		            MultiplayerSession.LocalUserID, transferId);
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
