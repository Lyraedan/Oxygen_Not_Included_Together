using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ONI_Together.Networking.Refresh
{
    internal sealed class TransportChunkAssembler
    {
        private sealed class Pending
        {
            public byte[][] Chunks;
            public int Received, Bytes;
            public double Updated;
        }
        private readonly Dictionary<(ulong?, int), Pending> pending = new();
        public void Clear() => pending.Clear();
        public byte[] Add(ulong? sender, int sequence, int index, int total, byte[] data, double now)
        {
            foreach (var expired in pending.Where(p => now - p.Value.Updated >= 30).Select(p => p.Key).ToArray()) pending.Remove(expired);
            if (total <= 0 || total > 65536 || index < 0 || index >= total || data == null || data.Length > 65536)
                throw new InvalidDataException("Invalid transport chunk");
            var key = (sender, sequence);
            if (!pending.TryGetValue(key, out var message))
            {
                if (pending.Count >= 64) throw new InvalidDataException("Too many pending transport messages");
                pending[key] = message = new Pending { Chunks = new byte[total][] };
            }
            if (message.Chunks.Length != total) throw new InvalidDataException("Transport chunk count changed");
            message.Updated = now;
            if (message.Chunks[index] == null)
            {
                if (message.Bytes > 64 * 1024 * 1024 - data.Length)
                {
                    pending.Remove(key);
                    throw new InvalidDataException("Transport message too large");
                }
                message.Chunks[index] = data;
                message.Received++;
                message.Bytes += data.Length;
            }
            if (message.Received != total) return null;
            pending.Remove(key);
            var assembled = new byte[message.Bytes];
            int offset = 0;
            foreach (var chunk in message.Chunks)
            {
                Buffer.BlockCopy(chunk, 0, assembled, offset, chunk.Length);
                offset += chunk.Length;
            }
            return assembled;
        }
    }
}
