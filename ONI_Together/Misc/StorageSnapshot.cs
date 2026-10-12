using System;
using System.Collections.Generic;
using System.IO;

namespace ONI_Together.Misc
{
    internal readonly struct StorageSnapshotEntry
    {
        public readonly int PrefabHash, DiseaseCount;
        public readonly float Mass, Temperature;
        public readonly byte DiseaseIdx;
        public StorageSnapshotEntry(int hash, float mass, float temperature, byte diseaseIdx, int diseaseCount)
        { PrefabHash = hash; Mass = mass; Temperature = temperature; DiseaseIdx = diseaseIdx; DiseaseCount = diseaseCount; }
    }

    internal static class StorageSnapshot
    {
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static List<StorageSnapshotEntry> Parse(byte[] blob)
        {
            if (blob == null || blob.Length < 8 || blob.Length > Networking.Refresh.RefreshLimits.RecordBytes)
                throw new InvalidDataException("Invalid storage snapshot length");
            using var reader = new BinaryReader(new MemoryStream(blob));
            float capacity = reader.ReadSingle();
            int count = reader.ReadInt32();
            if (!Finite(capacity) || capacity < 0 || count < 0 || count > (blob.Length - 8) / 17
                || blob.Length != 8L + count * 17L)
                throw new InvalidDataException("Invalid storage snapshot header");
            var entries = new List<StorageSnapshotEntry>(count);
            for (int i = 0; i < count; i++)
            {
                int hash = reader.ReadInt32();
                float mass = reader.ReadSingle(), temperature = reader.ReadSingle();
                byte disease = reader.ReadByte();
                int diseaseCount = reader.ReadInt32();
                if (!Finite(mass) || mass <= 0 || !Finite(temperature) || temperature < 1f || diseaseCount < 0
                    || disease == byte.MaxValue && diseaseCount != 0)
                    throw new InvalidDataException("Invalid stored stack");
                entries.Add(new StorageSnapshotEntry(hash, mass, temperature, disease, diseaseCount));
            }
            return entries;
        }

        // Null hashes identify protected/unrepresentable items, which are never matched or removed.
        public static int[] Match(IReadOnlyList<int?> existing, IReadOnlyList<StorageSnapshotEntry> incoming)
        {
            var byPrefab = new Dictionary<int, Queue<int>>();
            for (int i = 0; i < existing.Count; i++)
            {
                if (!existing[i].HasValue) continue;
                int hash = existing[i].Value;
                if (!byPrefab.TryGetValue(hash, out var indices)) byPrefab[hash] = indices = new Queue<int>();
                indices.Enqueue(i);
            }
            var matches = new int[incoming.Count];
            for (int i = 0; i < incoming.Count; i++)
                matches[i] = byPrefab.TryGetValue(incoming[i].PrefabHash, out var indices) && indices.Count > 0 ? indices.Dequeue() : -1;
            return matches;
        }
    }
}
