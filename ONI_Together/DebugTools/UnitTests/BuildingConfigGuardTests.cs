using System.IO;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class BuildingConfigGuardTests
	{
		[UnitTest(name: "Building config apply guard is nested", category: "Networking")]
		public static UnitTestResult ApplyGuardIsNested()
		{
			BuildingConfigPacket.ResetApplyingPacketForTests();
			try
			{
				BuildingConfigPacket.BeginApplyingPacket();
				BuildingConfigPacket.BeginApplyingPacket();
				BuildingConfigPacket.EndApplyingPacket();

				if (!BuildingConfigPacket.IsApplyingPacket)
					return UnitTestResult.Fail("First completion cleared a nested apply guard");

				BuildingConfigPacket.EndApplyingPacket();
				if (BuildingConfigPacket.IsApplyingPacket)
					return UnitTestResult.Fail("Apply guard remained set after the final completion");

				return UnitTestResult.Pass("Nested apply guard remains active until the outer apply completes");
			}
			finally
			{
				BuildingConfigPacket.ResetApplyingPacketForTests();
			}
		}

		[UnitTest(name: "Building config metadata and identity are bound", category: "Networking")]
		public static UnitTestResult MetadataAndIdentityAreBound()
		{
			if (!BuildingConfigPacket.IsValidMetadata(
				    -42, 123, -77, BuildingConfigType.Boolean, 0, -99, 1f, "state"))
				return UnitTestResult.Fail("Valid signed building metadata was rejected");

			if (BuildingConfigPacket.IsValidMetadata(
				    0, 123, -77, BuildingConfigType.Boolean, 0, -99, 1f, "state")
			    || BuildingConfigPacket.IsValidMetadata(
				    -42, 123, -77, (BuildingConfigType)255, 0, -99, 1f, "state")
			    || BuildingConfigPacket.IsValidMetadata(
				    -42, 123, -77, BuildingConfigType.Boolean, 0, -99, float.NaN, "state")
			    || BuildingConfigPacket.IsValidMetadata(
				    -42, 123, -77, BuildingConfigType.String, 0, -99, 1f, new string('x', 4097))
			    || BuildingConfigPacket.IsValidMetadata(
				    -42, 123, -77, BuildingConfigType.String, -1, int.MinValue, 1f, "state"))
				return UnitTestResult.Fail("Invalid building metadata was accepted");

			if (!BuildingConfigPacket.IdentityMatches(-42, 123, -77, -42, 123, -77)
			    || BuildingConfigPacket.IdentityMatches(-42, 123, -77, 42, 123, -77)
			    || BuildingConfigPacket.IdentityMatches(-42, 123, -77, -42, 124, -77)
			    || BuildingConfigPacket.IdentityMatches(-42, 123, -77, -42, 123, 77))
				return UnitTestResult.Fail("Building identity tuple is not strict");

			return BuildingConfigPacket.AllowsCellResolution(localIsHost: false, senderIsHost: true)
			       && !BuildingConfigPacket.AllowsCellResolution(localIsHost: true, senderIsHost: false)
				? UnitTestResult.Pass("Building config binds NetId, cell, and deterministic identity")
				: UnitTestResult.Fail("Host accepted client cell fallback");
		}

		[UnitTest(name: "Building config semantic primitives reject coercion", category: "Networking")]
		public static UnitTestResult SemanticPrimitivesRejectCoercion()
		{
			if (!BuildingConfigPacket.IsBooleanValue(0f)
			    || !BuildingConfigPacket.IsBooleanValue(1f)
			    || BuildingConfigPacket.IsBooleanValue(0.51f)
			    || !BuildingConfigPacket.IsIntegralValue(-77f)
			    || BuildingConfigPacket.IsIntegralValue(1.5f)
			    || !BuildingConfigPacket.IsInRange(5f, 0f, 5f)
			    || BuildingConfigPacket.IsInRange(-1f, 0f, 5f))
				return UnitTestResult.Fail("Boolean, integer, or range validation accepted coercion");

			return UnitTestResult.Pass("Semantic primitives require exact booleans, integers, and bounds");
		}

		[UnitTest(name: "Building config paired strings round-trip atomically", category: "Networking")]
		public static UnitTestResult PairedStringsRoundTripAtomically()
		{
			var packet = new BuildingConfigPacket
			{
				NetId = -42,
				Cell = 123,
				DeterministicBuildingId = -77,
				ConfigHash = 19,
				ConfigType = BuildingConfigType.String,
				StringValue = "entity",
				SecondaryStringValue = "filter"
			};

			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				packet.Serialize(writer);

			stream.Position = 0;
			var copy = new BuildingConfigPacket();
			using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
				copy.Deserialize(reader);

			return copy.StringValue == "entity" && copy.SecondaryStringValue == "filter"
				? UnitTestResult.Pass("Paired strings preserve one packet boundary")
				: UnitTestResult.Fail("Paired strings changed during serialization");
		}
	}
}
