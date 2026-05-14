using ONI_MP.DebugTools;
using ONI_MP.Networking.Packets.Architecture;
using Steamworks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shared.Profiling;
using UnityEngine;
using static PathFinder;
using static STRINGS.MISC;

namespace ONI_MP.Networking.Packets.Tools.Build
{
    public class BuildPacket : IPacket
    {
        private const int MaxMaterialTagCount = 64;

        private string PrefabID;
        private int Cell;
        private Orientation Orientation;
        private List<string> MaterialTags = new List<string>();
        private PrioritySetting Priority;
        private ObjectLayer ObjectLayer;

        public BuildPacket()
        {
        }

        public BuildPacket(string prefabID, int cell, Orientation orientation, IEnumerable<Tag> materials, ObjectLayer objectLayer)
        {
            using var _ = Profiler.Scope();

            PrefabID = prefabID;
            Cell = cell;
            Orientation  = orientation;
            MaterialTags = materials.Select(t => t.ToString()).ToList();

            if (PlanScreen.Instance)
                Priority = PlanScreen.Instance.GetBuildingPriority();

            ObjectLayer = objectLayer;
        }

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();

            writer.Write(PrefabID);
            writer.Write(Cell);
            writer.Write((int)Orientation);
            writer.Write(MaterialTags.Count);
            foreach (var tag in MaterialTags)
                writer.Write(tag);

            writer.Write((int)Priority.priority_class);
            writer.Write(Priority.priority_value);

            writer.Write((int)ObjectLayer);
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();

            PrefabID = reader.ReadString();
            Cell = reader.ReadInt32();
            Orientation = (Orientation)reader.ReadInt32();
            int count = reader.ReadInt32();
            if (count < 0 || count > MaxMaterialTagCount)
            {
                DebugConsole.LogWarning($"[BuildPacket] Invalid material tag count: {count}");
                Cell = Grid.InvalidCell;
                MaterialTags = [];
                return;
            }
            MaterialTags = new List<string>();
            for (int i = 0; i < count; i++)
                MaterialTags.Add(reader.ReadString());

            Priority = new PrioritySetting((PriorityScreen.PriorityClass)reader.ReadInt32(), reader.ReadInt32());
            ObjectLayer = (ObjectLayer) reader.ReadInt32();
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (!Grid.IsValidCell(Cell))
            {
                DebugConsole.LogWarning($"[BuildPacket] Invalid cell: {Cell}");
                return;
            }

            var def = Assets.GetBuildingDef(PrefabID);
            if (def == null)
            {
                DebugConsole.LogWarning($"[BuildPacket] Unknown building def: {PrefabID}");
                return;
            }
			var selected_elements = MaterialTags.Select(t => TagManager.Create(t)).ToList();
            Vector3 pos  = Grid.CellToPosCBC(Cell, Grid.SceneLayer.Building);
            GameObject visualizer = Util.KInstantiate(def.BuildingPreview, pos);

            GameObject builtItem = def.TryPlace(visualizer, pos, Orientation, selected_elements, "DEFAULT_FACADE"); // Build like normal;
            if (builtItem == null && def.ReplacementLayer != ObjectLayer.NumLayers) // Handle replacement
            {
                GameObject replacementCanidate = def.GetReplacementCandidate(Cell);
                if (replacementCanidate != null && !def.IsReplacementLayerOccupied(Cell))
                {
                    BuildingComplete component = replacementCanidate.GetComponent<BuildingComplete>();
                    if (component != null && component.Def.Replaceable && def.CanReplace(replacementCanidate))
                    {
                        Tag tag = replacementCanidate.GetComponent<PrimaryElement>().Element.tag;
                        if (tag.GetHash() == (int)SimHashes.StableSnow)
                            tag = SimHashes.Snow.CreateTag();
                        if (component.Def != def || selected_elements[0] != tag)
                        {
                            builtItem = def.TryReplaceTile(visualizer, pos, Orientation, selected_elements, "DEFAULT_FACADE");
                            Grid.Objects[Cell, (int)def.ReplacementLayer] = builtItem;
                        }
                    }
                }
            }
            SetPriority(builtItem);
            DebugConsole.Log("[BuildPacket] Built item " + def);
        }

        private void SetPriority(GameObject gameObject)
        {
            if (gameObject == null)
                return;

            Prioritizable prioritizable = gameObject?.GetComponent<Prioritizable>();
            prioritizable?.SetMasterPriority(Priority);
        }

        // TODO: Implement later when sandbox eventually gets done
        private void InstantBuild(BuildingDef def, List<Tag> selected_elements)
        {
            if (def == null) return;
            def.Build(Cell, Orientation, null, selected_elements, 295f, "DEFAULT_FACADE", playsound: false, GameClock.Instance.GetTime());
        }

    }
}
