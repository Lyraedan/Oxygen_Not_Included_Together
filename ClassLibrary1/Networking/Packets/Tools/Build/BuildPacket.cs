using ONI_MP.DebugTools;
using ONI_MP.Networking.Packets.Architecture;
using Steamworks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Networking.Packets.Tools.Build
{
    public class BuildPacket : IPacket
    {
        private const int MaxMaterialTagCount = 64;

        private string          PrefabID;
        private int             Cell;
        private Orientation     Orientation;
        private List<string>    MaterialTags = new List<string>();
        private PrioritySetting Priority;

        public BuildPacket()
        {
        }

        public BuildPacket(string prefabID, int cell, Orientation orientation, IEnumerable<Tag> materials)
        {
            using var _ = Profiler.Scope();

            PrefabID     = prefabID;
            Cell         = cell;
            Orientation  = orientation;
            MaterialTags = materials.Select(t => t.ToString()).ToList();

            if (PlanScreen.Instance)
                Priority = PlanScreen.Instance.GetBuildingPriority();
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
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();

            PrefabID    = reader.ReadString();
            Cell        = reader.ReadInt32();
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

            var     tags = MaterialTags.Select(t => new Tag(t)).ToList();
            Vector3 pos  = Grid.CellToPosCBC(Cell, Grid.SceneLayer.Building);

            GameObject visualizer = Util.KInstantiate(def.BuildingPreview, pos);
            GameObject gameObject = def.TryPlace(visualizer, pos, Orientation, tags, "DEFAULT_FACADE", false, (int) def.SceneLayer);


            Prioritizable prioritizable = gameObject?.GetComponent<Prioritizable>();
            prioritizable?.SetMasterPriority(Priority);

            // Instant build
            //def.Build(Cell, Orientation, null, tags, temp, "DEFAULT_FACADE", playsound: false, GameClock.Instance.GetTime());
        }
    }
}
