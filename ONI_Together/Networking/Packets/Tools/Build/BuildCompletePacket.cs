using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shared.Profiling;
using UnityEngine;
using ONI_Together.Networking.Components;
using Rendering;

namespace ONI_Together.Networking.Packets.Tools.Build
{
    public class BuildCompletePacket : IPacket
    {
        private const int MaxMaterialTagCount = 64;

        public int Cell;
        public string PrefabID;
        public Orientation Orientation;
        public List<string> MaterialTags = new List<string>();
        public float Temperature;
        public string FacadeID = "DEFAULT_FACADE";
        public int WorkerNetId;

        // Utility buildings
        public UtilityConnections UtilityConnectionFlags;

        public ObjectLayer ObjectLayer;

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();

            writer.Write(Cell);
            writer.Write(PrefabID);
            writer.Write((int)Orientation);
            writer.Write(Temperature);
            writer.Write(FacadeID);

            writer.Write(MaterialTags.Count);
            foreach (var tag in MaterialTags)
                writer.Write(tag);

            // Write connection flags
            writer.Write((int)UtilityConnectionFlags);

            writer.Write((int)ObjectLayer);

            writer.Write(WorkerNetId);
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();

            Cell = reader.ReadInt32();
            PrefabID = reader.ReadString();
            Orientation = (Orientation)reader.ReadInt32();
            Temperature = reader.ReadSingle();
            FacadeID = reader.ReadString();

            int count = reader.ReadInt32();
            if (count < 0 || count > MaxMaterialTagCount)
            {
                DebugConsole.LogWarning($"[BuildCompletePacket] Invalid material tag count: {count}");
                Cell = Grid.InvalidCell;
                MaterialTags = [];
                return;
            }
            MaterialTags = new List<string>(count);
            for (int i = 0; i < count; i++)
                MaterialTags.Add(reader.ReadString());

            UtilityConnectionFlags = (UtilityConnections)reader.ReadInt32();
            ObjectLayer = (ObjectLayer)reader.ReadInt32();

            WorkerNetId = reader.ReadInt32();
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (!Grid.IsValidCell(Cell))
            {
                DebugConsole.LogWarning($"[BuildCompletePacket] Invalid cell: {Cell}");
                return;
            }

            var def = Assets.GetBuildingDef(PrefabID);
            if (def == null)
            {
                DebugConsole.LogWarning($"[BuildCompletePacket] Unknown building def: {PrefabID}");
                return;
			}

			var tags = MaterialTags.Select(t => new Tag(t)).ToList();

            if (tags.Count == 0)
            {
                DebugConsole.LogWarning($"[BuildCompletePacket] No materials provided for {PrefabID} at cell {Cell}, using SandStone as fallback.");
                tags.Add(SimHashes.SandStone.CreateTag());
            }


			bool isBridge = def.BuildingComplete.GetComponent<ConduitBridgeBase>() || def.BuildingComplete.GetComponent<WireUtilityNetworkLink>() || def.BuildingComplete.GetComponent<LogicUtilityNetworkLink>() || PrefabID == ContactConductivePipeBridgeConfig.ID;
			int layerIndex = (int)ObjectLayer;
            // Destroy ghost/constructable if it still exists
            GameObject existing = Grid.Objects[Cell, layerIndex];

			if (existing == null && isBridge)
			{
				bool vertical = Orientation == Orientation.R90 || Orientation == Orientation.R270;
				//todo: account for other width bridges; get the offsets from bridge width instead
				int firstToCheck = vertical ? Grid.CellAbove(Cell) : Grid.CellLeft(Cell);
				int secondToCheck = vertical ? Grid.CellBelow(Cell) : Grid.CellRight(Cell);

				existing = FindMatchingBridgeConstructable(firstToCheck, layerIndex, PrefabID)
					?? FindMatchingBridgeConstructable(secondToCheck, layerIndex, PrefabID);
			}

			// Also clean up any replacement Constructable ghost (e.g. WireBridgeHighWattage or Tile replacing an existing tile)
			if (def.ReplacementLayer != ObjectLayer.NumLayers && def.ReplacementLayer != ObjectLayer)
			{
				int replLayer = (int)def.ReplacementLayer;
				GameObject replExisting = Grid.Objects[Cell, replLayer];
				if (replExisting != null && replExisting.TryGetComponent<Constructable>(out var replConstructable) && replConstructable != null)
				{
					replExisting.DeleteObject();
					Grid.Objects[Cell, replLayer] = null;
				}
			}

            if (existing != null)
            {
                // If the exact completed building is already present (e.g. duplicate packet or BuildingSyncer),
                // just ensure utility connections match without destroying and recreating it.
                if (existing.TryGetComponent<BuildingComplete>(out var existingComplete) &&
                    existing.TryGetComponent<KPrefabID>(out var existingKpid) &&
                    existingKpid.PrefabTag.Name == PrefabID &&
                    existingComplete.Orientation == Orientation)
                {
                    if ((int)UtilityConnectionFlags != 0)
                        ApplyUtilityConnections(existing, def);
                    return;
                }

                // Clean up ghost/constructable or replaced building using ONI's proper lifecycle
                // so automation port visualizers are removed synchronously.
                existing.DeleteObject();
                Grid.Objects[Cell, layerIndex] = null;
            }

            // Always build the completed building even if the ghost Constructable was missing on the client.
            float safeTemp = Temperature > 1f && !float.IsNaN(Temperature) && !float.IsInfinity(Temperature)
                ? Temperature
                : def.Temperature;
            string safeFacade = string.IsNullOrEmpty(FacadeID) ? "DEFAULT_FACADE" : FacadeID;

            try
            {
                var builtObj = def.Build(
                    Cell,
                    Orientation,
                    null,
                    tags,
                    safeTemp,
                    safeFacade,
                    playsound: false,
                    GameClock.Instance != null ? GameClock.Instance.GetTime() : 0f
                );

                // Apply wire/pipe connections for utility buildings
                if (builtObj != null && (int)UtilityConnectionFlags != 0)
                {
                    ApplyUtilityConnections(builtObj, def);
                }

                // Ensure tile visuals refresh immediately on clients after def.Build
                if (def.TileLayer != ObjectLayer.NumLayers)
                {
                    TileVisualizer.RefreshCell(Cell, def.TileLayer, def.ReplacementLayer);
                }
            }
            catch (System.Exception ex)
            {
                DebugConsole.LogError($"[BuildCompletePacket] Failed to build {PrefabID} at cell {Cell}: {ex}");
            }

            DebugConsole.Log($"[BuildCompletePacket] Finalized {PrefabID} at cell {Cell}");
        }

        private static GameObject FindMatchingBridgeConstructable(int checkCell, int layerIndex, string expectedPrefabId)
        {
            if (!Grid.IsValidCell(checkCell))
                return null;

            GameObject candidate = Grid.Objects[checkCell, layerIndex];
            if (candidate == null)
                return null;

            if (candidate.TryGetComponent<Constructable>(out _) &&
                candidate.TryGetComponent<Building>(out var building) &&
                building.Def != null &&
                building.Def.PrefabID == expectedPrefabId)
            {
                return candidate;
            }

            return null;
        }

        private void ApplyUtilityConnections(GameObject go, BuildingDef def)
        {
            // Neighbours are baked into the conduit managers
            if (go.TryGetComponent<KAnimGraphTileVisualizer>(out var vis))
            {
                vis.UpdateConnections(UtilityConnectionFlags);
                vis.Refresh();
            }
        }

        private void ApplyUtilityConnections(KAnimGraphTileVisualizer vis, UtilityConnections flags)
        {
            vis.UpdateConnections(flags);
            vis.Refresh();
        }
    }
}

