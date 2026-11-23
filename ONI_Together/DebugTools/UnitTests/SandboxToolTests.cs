using System.IO;
using System.Text;
using ONI_Together.Networking.Packets.Tools.Sandbox;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
    public static class SandboxToolTests
    {
        [UnitTest(name: "Sandbox tool packet round-trip", category: "SandboxTools")]
        public static UnitTestResult SandboxToolPacketRoundTrip()
        {
            var input = new SandboxToolPacket
            {
                Action = SandboxToolAction.StoryTrait,
                Cell = 1234,
                DistanceFromOrigin = 3,
                Position = new Vector3(12.5f, 8.25f, 0f),
                ElementIndex = 42,
                DiseaseCount = 900,
                MoraleAdjustment = -5,
                Mass = 100f,
                Temperature = 325.5f,
                TemperatureAdditive = 12f,
                StressAdditive = -20f,
                DiseaseId = "FoodPoisoning",
                EntityId = "Hatch",
                StoryId = "LonelyMinion"
            };

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                input.Serialize(writer);

            stream.Position = 0;
            var output = new SandboxToolPacket();
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                output.Deserialize(reader);

            if (output.Action != input.Action || output.Cell != input.Cell || output.Position != input.Position)
                return UnitTestResult.Fail("Sandbox action location did not round-trip");
            if (output.ElementIndex != input.ElementIndex || output.DiseaseCount != input.DiseaseCount)
                return UnitTestResult.Fail("Sandbox material settings did not round-trip");
            if (output.EntityId != input.EntityId || output.StoryId != input.StoryId)
                return UnitTestResult.Fail("Sandbox entity settings did not round-trip");

            return UnitTestResult.Pass("Sandbox tool action and settings round-trip correctly");
        }
    }
}
