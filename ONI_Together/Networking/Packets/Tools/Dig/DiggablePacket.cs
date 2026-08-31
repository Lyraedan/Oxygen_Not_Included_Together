using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Tools.Dig
{
    public class DiggablePacket : IPacket
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

            // A client still in the menu, or part way through downloading the save, has no world
            // yet and DigTool.PlaceDig throws straight through it. Measured over one join: 64
            // "Failed to handle incoming packet: NullReferenceException at DigTool.PlaceDig"
            // before this guard, 0 after - with the guard itself hit 78 times, so the condition
            // is just as frequent either way.
            //
            // The dig order is lost either way, and nothing else is: PacketHandler.HandleIncoming
            // is called inside a per-message try/catch (SteamworksClient.ProcessIncomingMessages),
            // so a throwing packet costs only itself. What this buys is a one-line warning in
            // place of a multi-frame stack dump, and an explicit statement that dropping the
            // order is the intended behaviour rather than an accident.
            if (!Utils.IsInGame() || !Grid.IsValidCell(Cell))
            {
                DebugConsole.LogWarning(
                    $"[DiggablePacket] Dropped dig for cell {Cell}: no world loaded yet");
                return;
            }

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
