using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using UnityEngine;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.Tools.Dig
{
    public class DiggablePacket : IPacket, IClientRelayable
    {
        /// <summary>
        /// Gets a value indicating whether incoming messages are currently being processed.
        /// Use in patches to prevent recursion when applying tool changes.
        /// </summary>
        public static bool ProcessingIncoming { get; private set; }

        private int             Cell;
        private int             AnimationDelay;
        private PrioritySetting Priority;

        public DiggablePacket()
        {
        }

        public DiggablePacket(int cell, int animationDelay)
        {
            using var _ = Profiler.Scope();

            Cell           = cell;
            AnimationDelay = animationDelay;
        }

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();

            if (ToolMenu.Instance?.PriorityScreen != null)
                Priority = ToolMenu.Instance.PriorityScreen.GetLastSelectedPriority();

            writer.Write(Cell);
            writer.Write(AnimationDelay);
            writer.Write((int)Priority.priority_class);
            writer.Write(Priority.priority_value);
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();

            Cell           = reader.ReadInt32();
            AnimationDelay = reader.ReadInt32();
            Priority       = new PrioritySetting((PriorityScreen.PriorityClass)reader.ReadInt32(), reader.ReadInt32());
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            GameObject game_object;
            ProcessingIncoming = true;
            try
            {
                game_object = DigTool.PlaceDig(Cell, AnimationDelay);
            }
            finally
            {
                ProcessingIncoming = false;
            }

            Prioritizable prioritizable = game_object?.GetComponent<Prioritizable>();
            prioritizable?.SetMasterPriority(Priority);
        }
    }
}
