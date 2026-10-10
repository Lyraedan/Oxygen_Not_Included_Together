using System.IO;
using System.Linq;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Tools.Prioritize;

namespace ONI_Together.DebugTools.UnitTests
{
    public static class PriorityPacketTests
    {
        [UnitTest(name: "Priority relay preserves sender priority and filters", category: "Networking")]
        public static UnitTestResult RelayPreservesSelection()
        {
            var tool = PrioritizeTool.Instance;
            var screen = ToolMenu.Instance?.PriorityScreen;
            if (tool == null || screen == null || tool.currentFilters.Length == 0)
                return UnitTestResult.Fail("Load a colony with the Priority tool initialized first");

            // Represent a client selecting one filter and priority 9 while the
            // receiving host has priority 1 and all filters enabled.
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(1);
                writer.Write(tool.currentFilters[0].name);
                writer.Write(123);
                writer.Write(0);
                writer.Write((int)PriorityScreen.PriorityClass.basic);
                writer.Write(9);
            }

            var originalBytes = stream.ToArray();
            stream.Position = 0;
            var received = new PrioritizePacket();
            using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
                received.Deserialize(reader);

            var priorityField = Traverse.Create(screen).Field("lastSelectedPriority");
            var savedPriority = screen.GetLastSelectedPriority();
            var savedFilters = tool.currentFilters.Select(filter => filter.state).ToArray();
            try
            {
                priorityField.SetValue(new PrioritySetting(PriorityScreen.PriorityClass.basic, 1));
                foreach (var filter in tool.currentFilters)
                    filter.state = ToolParameterMenu.ToggleState.On;

                if (!originalBytes.SequenceEqual(received.SerializeToByteArray()))
                    return UnitTestResult.Fail("Relaying replaced the sender's priority or filters");

                return UnitTestResult.Pass("Relay preserves the client's exact tool selection");
            }
            finally
            {
                priorityField.SetValue(savedPriority);
                for (int i = 0; i < savedFilters.Length; i++)
                    tool.currentFilters[i].state = savedFilters[i];
            }
        }

        [UnitTest(name: "Priority fan-out uses one consistent selection", category: "Networking")]
        public static UnitTestResult RepeatedSendPreservesSelection()
        {
            var tool = PrioritizeTool.Instance;
            var screen = ToolMenu.Instance?.PriorityScreen;
            if (tool == null || screen == null || tool.currentFilters.Length == 0)
                return UnitTestResult.Fail("Load a colony with the Priority tool initialized first");

            var priorityField = Traverse.Create(screen).Field("lastSelectedPriority");
            var savedPriority = screen.GetLastSelectedPriority();
            var savedFilters = tool.currentFilters.Select(filter => filter.state).ToArray();
            try
            {
                priorityField.SetValue(new PrioritySetting(PriorityScreen.PriorityClass.basic, 9));
                foreach (var filter in tool.currentFilters)
                    filter.state = ToolParameterMenu.ToggleState.Off;

                var packet = new PrioritizePacket { cell = 123, distFromOrigin = 0 };
                var firstSend = packet.SerializeToByteArray();

                priorityField.SetValue(new PrioritySetting(PriorityScreen.PriorityClass.basic, 1));
                foreach (var filter in tool.currentFilters)
                    filter.state = ToolParameterMenu.ToggleState.On;

                if (!firstSend.SequenceEqual(packet.SerializeToByteArray()))
                    return UnitTestResult.Fail("Sending the same action again changed its selection");

                return UnitTestResult.Pass("Repeated sends preserve priority and filters");
            }
            finally
            {
                priorityField.SetValue(savedPriority);
                for (int i = 0; i < savedFilters.Length; i++)
                    tool.currentFilters[i].state = savedFilters[i];
            }
        }
    }
}
