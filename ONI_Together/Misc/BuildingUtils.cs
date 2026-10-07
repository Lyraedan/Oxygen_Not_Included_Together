using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ONI_Together.DebugTools;
using UnityEngine;
using static LogicGateVisualizer;

namespace ONI_Together.Misc
{
    public static class BuildingUtils
    {
        public static bool ValidCell(GameObject visualizer, BuildingDef def, int cell, Orientation orientation)
        {
            if (Grid.IsValidCell(cell)
                && Grid.IsVisible(cell))
            {
                bool IsValidPlaceLocation = def.IsValidPlaceLocation(visualizer, cell, orientation, out string failReason);
                bool IgnorableFailReason =
                    failReason == global::STRINGS.UI.TOOLTIPS.HELP_BUILDLOCATION_WALL
                    || failReason == global::STRINGS.UI.TOOLTIPS.HELP_BUILDLOCATION_CORNER
                    || failReason == global::STRINGS.UI.TOOLTIPS.HELP_BUILDLOCATION_CORNER_FLOOR
                    || (failReason == global::STRINGS.UI.TOOLTIPS.HELP_BUILDLOCATION_BACK_WALL_REQUIRED);

                bool validCell = (IsValidPlaceLocation || IgnorableFailReason);
                bool replacement = false;
                return (validCell || replacement);
            }

            return false;
        }

        public static void EncodeStorageContents(Storage storage, Dictionary<string, Variant> optionalValues, string keyPrefix = "")
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(storage.capacityKg);

            var validItems = new List<GameObject>();
            for (int i = 0; i < storage.items.Count; i++)
            {
                var go = storage.items[i];
                if (go == null) continue;
                var pe = go.GetComponent<PrimaryElement>();
                if (pe == null || pe.Mass <= 0f) continue;
                if (!go.TryGetComponent<KPrefabID>(out _)) continue;
                if (IsProtectedStorageItem(go)) continue;
                validItems.Add(go);
            }

            writer.Write(validItems.Count);
            foreach (var go in validItems)
            {
                var pe = go.GetComponent<PrimaryElement>();
                var prefabID = go.GetComponent<KPrefabID>();
                writer.Write(prefabID.PrefabTag.GetHashCode());
                writer.Write(pe.Mass);
                writer.Write(pe.Temperature);
                writer.Write(pe.DiseaseIdx);
                writer.Write(pe.DiseaseCount);
            }

            optionalValues[keyPrefix + "stor"] = ms.ToArray();
        }

        public static void RebuildStorageFromData(Storage storage, Dictionary<string, Variant> data, string keyPrefix = "", string diseaseReason = "Multiplayer Sync")
        {
            if (storage == null) return;

            if (data.TryGetValue(keyPrefix + "stor", out var blobVar) && blobVar.ByteArray != null)
            {
                RebuildFromBlob(storage, blobVar.ByteArray, diseaseReason);
                return;
            }
            
            DebugConsole.LogError($"[Storage/RebuildStorageFromData] Failed to rebuild storage from data! Key: {keyPrefix + "stor"} not found!");
        }

        private static void RebuildFromBlob(Storage storage, byte[] blob, string diseaseReason)
        {
            // Parse and resolve every prefab before touching the container.
            var entries = StorageSnapshot.Parse(blob);
            var prefabs = new GameObject[entries.Count];
            var elements = new Element[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                var tag = new Tag(entries[i].PrefabHash);
                elements[i] = ElementLoader.GetElement(tag);
                if (elements[i] != null)
                {
                    if (elements[i].substance == null || elements[i].id == SimHashes.Vacuum)
                        throw new InvalidDataException("Invalid stored element");
                    continue;
                }
                prefabs[i] = Assets.GetPrefab(tag);
                if (prefabs[i] == null || !prefabs[i].TryGetComponent<PrimaryElement>(out _) || IsProtectedStorageItem(prefabs[i]))
                    throw new InvalidDataException("Unknown or protected stored prefab");
            }

            var existing = storage.items.ToArray();
            var hashes = new int?[existing.Length];
            for (int i = 0; i < existing.Length; i++)
                if (existing[i] != null && !IsProtectedStorageItem(existing[i])
                    && existing[i].TryGetComponent<KPrefabID>(out var id) && existing[i].TryGetComponent<PrimaryElement>(out _))
                    hashes[i] = id.PrefabTag.GetHashCode();
            var matches = StorageSnapshot.Match(hashes, entries);
            var retained = new HashSet<int>();
            for (int i = 0; i < entries.Count; i++)
            {
                int match = matches[i];
                var entry = entries[i];
                var item = match >= 0 ? existing[match]
                    : elements[i] != null ? elements[i].substance.SpawnResource(storage.transform.position, entry.Mass,
                        entry.Temperature, entry.DiseaseIdx, entry.DiseaseCount)
                    : GameUtil.KInstantiate(prefabs[i], storage.transform.position, Grid.SceneLayer.Ore);
                var pe = item.GetComponent<PrimaryElement>();
                pe.Mass = entry.Mass;
                pe.Temperature = entry.Temperature;
                pe.ModifyDiseaseCount(-pe.DiseaseCount, diseaseReason);
                if (entry.DiseaseIdx != byte.MaxValue && entry.DiseaseCount > 0)
                    pe.AddDisease(entry.DiseaseIdx, entry.DiseaseCount, diseaseReason);
                if (match >= 0) retained.Add(match);
                else
                {
                    item.SetActive(true);
                    // Normal Store absorbs compatible stacks, changing quantities and extra item state.
                    // The deserialization path keeps the host's individual stacks and matched identities.
                    storage.Store(item, hide_popups: true, block_events: true, do_disease_transfer: false, is_deserializing: true);
                    storage.ApplyStoredItemModifiers(item, true, false);
                }
            }
            for (int i = 0; i < existing.Length; i++)
                if (hashes[i].HasValue && !retained.Contains(i))
                {
                    storage.Remove(existing[i], false);
                    existing[i].DeleteObject();
                }
            storage.items.RemoveAll(item => item == null || item.IsNullOrDestroyed());
        }

        // Preservation rules adapted from Kyle Yi (younatics)'s PR #175:
        // https://github.com/Lyraedan/Oxygen_Not_Included_Together/pull/175
        // The bulk blob cannot describe ownership, suit durability/oxygen, or an animal's life state.
        private static bool IsProtectedStorageItem(GameObject go) => go != null
            && (go.GetComponent<Assignable>() != null || go.HasTag(GameTags.Creature));
        
        // UP = Utility Path
        private const int UP_FIRST_CELL_BITS = 22;
        private const int UP_SEG_BITS = 4;
        private const int UP_SEG_COUNT_BITS = 2;
        private const int UP_MAX_SEGMENTS = 2;
        private const int UP_MAX_LEN_PER_SEG = 4;
        private const int UP_MAX_CELLS_PER_CHUNK = 1 + UP_MAX_SEGMENTS * UP_MAX_LEN_PER_SEG; // 9
        
        // Derived bit masks/shifts
        private const int UP_FIRST_CELL_MASK = (1 << UP_FIRST_CELL_BITS) - 1;
        private const int UP_SEGMENTS_BITS = UP_SEG_BITS * UP_MAX_SEGMENTS;
        private const int UP_SEGMENTS_MASK = (1 << UP_SEGMENTS_BITS) - 1;
        private const int UP_SEGMENTS_SHIFT = UP_FIRST_CELL_BITS;
        private const int UP_SEG_COUNT_MASK = (1 << UP_SEG_COUNT_BITS) - 1;
        private const int UP_SEG_COUNT_SHIFT = UP_FIRST_CELL_BITS + UP_SEGMENTS_BITS;
        
        /// <summary>
        /// Encodes a utility build path into an array of 32-bit chunks, each packing up to 9 cells.
        /// Bits 0-21: firstCell index. Bits 22-29: up to 2 direction-run segments (4-bit each:
        /// 2-bit direction + 2-bit run length-1). Bits 30-31: segment count.
        /// </summary>
        public static uint[] EncodeUtilityPath(List<BaseUtilityBuildTool.PathNode> path)
        {
            if (path == null || path.Count <= 1)
                return null;

            List<uint> chunks = new List<uint>();
            int pos = 0;
            int count = path.Count;

            while (pos < count)
            {
                int chunkEnd = pos + UP_MAX_CELLS_PER_CHUNK;
                if (chunkEnd > count)
                    chunkEnd = count;

                int chunkSize = chunkEnd - pos;
                if (chunkSize <= 1)
                    break;

                int firstCell = path[pos].cell;
                uint data = (uint)(firstCell & UP_FIRST_CELL_MASK);

                int segmentsPacked = 0;
                int segmentCount = 0;
                int i = pos + 1;

                while (i < chunkEnd && segmentCount < UP_MAX_SEGMENTS)
                {
                    int from = path[i - 1].cell;
                    int to = path[i].cell;
                    UtilityConnections dir = UtilityConnectionsExtensions.DirectionFromToCell(from, to);
                    if (dir == (UtilityConnections)0)
                        break;

                    int dirIndex;
                    if (dir == UtilityConnections.Right) dirIndex = 0;
                    else if (dir == UtilityConnections.Up) dirIndex = 1;
                    else if (dir == UtilityConnections.Left) dirIndex = 2;
                    else dirIndex = 3;

                    int len = 1;
                    i++;
                    while (i < chunkEnd && len < UP_MAX_LEN_PER_SEG)
                    {
                        int prev = path[i - 1].cell;
                        int curr = path[i].cell;
                        if (UtilityConnectionsExtensions.DirectionFromToCell(prev, curr) != dir)
                            break;
                        len++;
                        i++;
                    }

                    int seg = (dirIndex & UP_SEG_COUNT_MASK) | (((len - 1) & UP_SEG_COUNT_MASK) << UP_SEG_COUNT_BITS);
                    segmentsPacked |= seg << (segmentCount * UP_MAX_LEN_PER_SEG);
                    segmentCount++;
                }

                data |= (uint)(segmentsPacked & UP_SEGMENTS_MASK) << UP_SEGMENTS_SHIFT;
                data |= (uint)(segmentCount & UP_SEG_COUNT_MASK) << UP_SEG_COUNT_SHIFT;

                chunks.Add(data);
                pos = i;
            }

            return chunks.ToArray();
        }
        
        /// <summary>
        /// Decodes an array of 9-cell chunk uints back into a flat int[] of Grid cell indices.
        /// Each chunk is decoded via DecodeChunk and concatenated in order.
        /// </summary>
        public static int[] DecodeUtilityPath(uint[] pathData)
        {
            if (pathData == null || pathData.Length == 0)
                return null;

            List<int> cells = new List<int>(pathData.Length * UP_MAX_CELLS_PER_CHUNK);

            foreach (uint chunk in pathData)
            {
                if (chunk == 0)
                    continue;

                int[] chunkCells = DecodeUtilityPathChunk(chunk);
                if (chunkCells != null)
                    cells.AddRange(chunkCells);
            }

            return cells.ToArray();
        }

        /// <summary>
        /// Encodes a utility build path into an array of 64-bit chunks. Lower 32 bits = path data
        /// (same as EncodeUtilityPath). Upper 32 bits = validity bitmask (bits 0-8 for up to 9 cells).
        /// </summary>
        public static ulong[] EncodeUtilityPathWithValidity(List<BaseUtilityBuildTool.PathNode> path)
        {
            if (path == null || path.Count <= 1)
                return null;

            List<ulong> chunks = new List<ulong>();
            int pos = 0;
            int count = path.Count;

            while (pos < count)
            {
                int chunkEnd = pos + UP_MAX_CELLS_PER_CHUNK;
                if (chunkEnd > count)
                    chunkEnd = count;

                uint validityMask = 0;
                for (int j = pos; j < chunkEnd; j++)
                {
                    if (path[j].valid)
                        validityMask |= 1u << (j - pos);
                }

                int firstCell = path[pos].cell;
                uint data = (uint)(firstCell & UP_FIRST_CELL_MASK);

                int segmentsPacked = 0;
                int segmentCount = 0;
                int i = pos + 1;

                while (i < chunkEnd && segmentCount < UP_MAX_SEGMENTS)
                {
                    int from = path[i - 1].cell;
                    int to = path[i].cell;
                    UtilityConnections dir = UtilityConnectionsExtensions.DirectionFromToCell(from, to);
                    if (dir == (UtilityConnections)0)
                        break;

                    int dirIndex;
                    if (dir == UtilityConnections.Right) dirIndex = 0;
                    else if (dir == UtilityConnections.Up) dirIndex = 1;
                    else if (dir == UtilityConnections.Left) dirIndex = 2;
                    else dirIndex = 3;

                    int len = 1;
                    i++;
                    while (i < chunkEnd && len < UP_MAX_LEN_PER_SEG)
                    {
                        int prev = path[i - 1].cell;
                        int curr = path[i].cell;
                        if (UtilityConnectionsExtensions.DirectionFromToCell(prev, curr) != dir)
                            break;
                        len++;
                        i++;
                    }

                    int seg = (dirIndex & UP_SEG_COUNT_MASK) | (((len - 1) & UP_SEG_COUNT_MASK) << UP_SEG_COUNT_BITS);
                    segmentsPacked |= seg << (segmentCount * UP_MAX_LEN_PER_SEG);
                    segmentCount++;
                }

                data |= (uint)(segmentsPacked & UP_SEGMENTS_MASK) << UP_SEGMENTS_SHIFT;
                data |= (uint)(segmentCount & UP_SEG_COUNT_MASK) << UP_SEG_COUNT_SHIFT;

                chunks.Add(((ulong)validityMask << 32) | data);
                pos = i;
            }

            return chunks.ToArray();
        }

        /// <summary>
        /// Decodes a single 32-bit chunk into an array of Grid cell indices.
        /// Bits 0–21: firstCell. Bits 22–29: up to two 4-bit direction-run segments
        /// (2-bit direction, 2-bit run length − 1). Bits 30–31: segment count.
        /// Reconstructs cells by walking from firstCell through each direction-run.
        /// Returns null if data is 0 or firstCell is invalid.
        /// </summary>
        public static int[] DecodeUtilityPathChunk(uint data)
        {
            if (data == 0)
                return null;

            int firstCell = (int)(data & ((1 << UP_FIRST_CELL_BITS) - 1));
            int segmentsPacked = (int)((data >> UP_FIRST_CELL_BITS) & ((1 << (UP_SEG_BITS * UP_MAX_SEGMENTS)) - 1));
            int segmentCount = (int)((data >> (UP_FIRST_CELL_BITS + UP_SEG_BITS * UP_MAX_SEGMENTS)) & ((1 << UP_SEG_COUNT_BITS) - 1));

            if (!Grid.IsValidCell(firstCell))
                return null;

            List<int> cells = new List<int>(UP_MAX_CELLS_PER_CHUNK);
            cells.Add(firstCell);
            int cell = firstCell;

            for (int s = 0; s < segmentCount && s < UP_MAX_SEGMENTS; s++)
            {
                int seg = (segmentsPacked >> (s * UP_SEG_BITS)) & 0xF;
                int dir = seg & 0x3;
                int len = ((seg >> 2) & 0x3) + 1;

                int delta;
                switch (dir)
                {
                    case 0: delta = 1; break;
                    case 1: delta = Grid.WidthInCells; break;
                    case 2: delta = -1; break;
                    case 3: delta = -Grid.WidthInCells; break;
                    default: continue;
                }

                for (int i = 0; i < len; i++)
                {
                    cell += delta;
                    if (!Grid.IsValidCell(cell))
                        break;
                    cells.Add(cell);
                }
            }

            return cells.ToArray();
        }

        /// <summary>
        /// Iterates every ILogicUIElement in uiVisElements and removes any whose
        /// cell has no Building component on any object layer. Called periodically
        /// to sweep up orphaned port entries that survive the normal cleanup path.
        /// </summary>
        public static void CleanupOrphanedLogicVisElements()
        {
            var mgr = Game.Instance.logicCircuitManager;
            var elems = mgr.GetVisElements();
            for (int i = elems.Count - 1; i >= 0; i--)
            {
                var elem = elems[i];
                int cell = elem.GetLogicUICell();
                if (!Grid.IsValidCell(cell))
                    continue;

                bool hasBuilding = false;
                foreach (var layer in Grid.ObjectLayers)
                {
                    if (layer.TryGetValue(cell, out var obj) && obj != null)
                    {
                        if (obj.GetComponent<Building>() != null)
                        {
                            hasBuilding = true;
                            break;
                        }
                    }
                }

                if (!hasBuilding)
                    mgr.RemoveVisElem(elem);
            }
        }

    }
}
