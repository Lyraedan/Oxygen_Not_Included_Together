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

            if(existing == null && isBridge)
            {
                bool vertical = Orientation == Orientation.R90 || Orientation == Orientation.R270;
                //todo: account for other width bridges; get the offsets from bridge width instead
                int firstToCheck = vertical ? Grid.CellAbove(Cell) : Grid.CellLeft(Cell);
                int secondToCheck = vertical ? Grid.CellBelow(Cell) : Grid.CellRight(Cell);

				existing = Grid.Objects[firstToCheck, layerIndex];
                if(existing == null)
					existing = Grid.Objects[secondToCheck, layerIndex];
			}

            if (existing != null)
            {
                //if (existing.TryGetComponent<Constructable>(out Constructable con))
                //{
                //    if (NetworkIdentityRegistry.TryGet(WorkerNetId, out var identity) &&
                //       identity.TryGetComponent<WorkerBase>(out var worker))
                //    {
                //        con.initialTemperature = Temperature;
                //        con.SelectedElementsTags = tags;
                //        con.FinishConstruction(UtilityConnectionFlags, worker);
                //    }
                //}
                //else
                {
                    // Clean up dangling visualizers
                    Object.Destroy(existing);
                    Grid.Objects[Cell, layerIndex] = null;

                    var builtObj = def.Build(
                        Cell,
                        Orientation,
                        null,
                        tags,
                        Temperature,
                        FacadeID,
                        playsound: false,
                        GameClock.Instance.GetTime()
                    );

                    // Apply wire/pipe connections for utility buildings
                    if (builtObj != null && (int)UtilityConnectionFlags != 0)
                    {
                        ApplyUtilityConnections(builtObj, def);
                    }
                }
            }

            DebugConsole.Log($"[BuildCompletePacket] Finalized {PrefabID} at cell {Cell}");
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

