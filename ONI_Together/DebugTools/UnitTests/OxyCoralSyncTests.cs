using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.Aquatic;
using ONI_Together.Patches.DLC.Aquatic;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class OxyCoralSyncTests
	{
		[UnitTest(name: "OxyCoral bubble outcome is bounded host state", category: "Sync")]
		public static UnitTestResult BubbleOutcomeRoundtrip()
		{
			var input = new OxyCoralBubblePacket
			{
				WorldId = 3,
				SourceCell = 100,
				OutputCell = 101,
				Sequence = 7,
				Mass = 0.2f,
				Temperature = 295f
			};
			var output = new OxyCoralBubblePacket();
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				input.Serialize(writer);
			stream.Position = 0;
			using var reader = new BinaryReader(stream);
			output.Deserialize(reader);

			if (output.WorldId != 3 || output.SourceCell != 100 || output.OutputCell != 101 ||
			    output.Sequence != 7 || output.Mass != 0.2f || output.Temperature != 295f ||
			    stream.Position != stream.Length || output is not IHostOnlyPacket)
				return UnitTestResult.Fail("OxyCoral bubble outcome did not roundtrip");
			if (!OxyCoralBubblePacket.ShouldApply(false, true) ||
			    OxyCoralBubblePacket.ShouldApply(true, true) ||
			    OxyCoralBubblePacket.ShouldApply(false, false) ||
			    OxyCoralSync.ShouldRunProduction(true, false))
				return UnitTestResult.Fail("OxyCoral authority gate is incorrect");

			input.Mass = OxyCoralBubblePacket.MaxMass + 1f;
			if (input.IsWireValid())
				return UnitTestResult.Fail("Unbounded OxyCoral bubble mass was accepted");
			return UnitTestResult.Pass("OxyCoral random output is sequenced bounded host state");
		}
	}
}
