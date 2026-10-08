using System;
using System.Collections.Generic;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using UnityEngine;

namespace ONI_Together.Networking.Transport
{
    public abstract class TransportPacketSender
    {
        private readonly Dictionary<object, Queue<(IPacket packet, PacketSendMode sendMode)>> _pendingQueues = new Dictionary<object, Queue<(IPacket packet, PacketSendMode sendMode)>>();
        private readonly List<object> _emptyConnections = new List<object>();

        public bool SendToConnection(object conn, IPacket packet, PacketSendMode sendType = PacketSendMode.ReliableImmediate)
        {
			// Never put latency-sensitive snapshots behind reliable state traffic.
			// Old movement data has no value and causes visible catch-up/teleports.
			if (!Configuration.Instance.EnablePacketQueue || IsLatencySensitive(sendType)
				|| packet is ILatencySensitivePacket)
                return SendPacket(conn, packet, sendType);
            // queue it
            if (!_pendingQueues.TryGetValue(conn, out var queue))
                _pendingQueues[conn] = queue = new();

            // Given the nature of the game and the sync. I'm not sure this is a good idea for late game colonies
            //int MAX_QUEUE_DEPTH = 1000; // After 1000 packets. Discard oldest
            //if (queue.Count >= MAX_QUEUE_DEPTH)
            //    queue.Dequeue();

            queue.Enqueue((packet, sendType));
            return true;
        }

		internal static bool IsLatencySensitive(PacketSendMode sendType)
		{
			return (sendType & PacketSendMode.Reliable) == 0
				&& (sendType & PacketSendMode.NoDelay) != 0;
		}

        public void Flush()
        {
            if (!Configuration.Instance.EnablePacketQueue)
                return;

            int maxThisTick = (int)(Configuration.Instance.MaxPacketsPerSecond * Time.unscaledDeltaTime);
            //maxThisTick = Mathf.Clamp(maxThisTick, 1, 60); // never more than 60 per frame
            if (maxThisTick < 1) maxThisTick = 1;

            _emptyConnections.Clear();
            foreach (var kvp in _pendingQueues)
            {
                int sent = 0;
                while (kvp.Value.Count > 0 && sent < maxThisTick)
                {
                    var (packet, sendType) = kvp.Value.Dequeue();
                    SendPacket(kvp.Key, packet, sendType);
                    sent++;
                }
                if (kvp.Value.Count == 0)
                    _emptyConnections.Add(kvp.Key);
            }

            foreach (var key in _emptyConnections)
                _pendingQueues.Remove(key);
        }

        public abstract bool SendPacket(object conn, IPacket packet, PacketSendMode sendType = PacketSendMode.ReliableImmediate);

        private const int MAX_PAYLOAD_BYTES = 1000;

        /// <summary>
        /// Payloads larger than MAX_PAYLOAD_BYTES are split into ChunkedPacket fragments before hitting the transport, so backends
        /// with a hard single-message cap (e.g. LiteNetLib's 1023-byte unreliable limit or Riptides 1024-byte limit)
        /// never throw. sendRaw is provided by the transport to emit one serialized payload.
        /// </summary>
        protected bool SendChunkedIfNeeded(object conn, byte[] bytes, IPacket packet, PacketSendMode sendType, Func<object, byte[], IPacket, PacketSendMode, bool> sendRaw)
        {
            if (bytes.Length <= MAX_PAYLOAD_BYTES || packet is ChunkedPacket)
                return sendRaw(conn, bytes, packet, sendType);

            int chunkDataSize = MAX_PAYLOAD_BYTES - 20; // overhead for ChunkedPacket header
            int totalChunks = (bytes.Length + chunkDataSize - 1) / chunkDataSize;
            int sequenceId = ChunkedPacket.GetNextSequenceId();

            for (int i = 0; i < totalChunks; i++)
            {
                int offset = i * chunkDataSize;
                int length = Math.Min(chunkDataSize, bytes.Length - offset);
                byte[] chunkData = new byte[length];
                Array.Copy(bytes, offset, chunkData, 0, length);

                var chunk = new ChunkedPacket
                {
                    SequenceId = sequenceId,
                    ChunkIndex = i,
                    TotalChunks = totalChunks,
                    ChunkData = chunkData
                };

                byte[] chunkBytes = PacketSender.SerializePacketForSending(chunk);
                if (!sendRaw(conn, chunkBytes, chunk, sendType))
                    return false;
            }

            return true;
        }

    }
}
