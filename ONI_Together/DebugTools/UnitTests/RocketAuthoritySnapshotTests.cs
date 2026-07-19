using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using ONI_Together.Patches.DLC.SpacedOut;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class RocketAuthoritySnapshotTests
	{
		[UnitTest(name: "Spaced Out rocket authority snapshot roundtrips", category: "Sync")]
		public static UnitTestResult AuthoritySnapshotRoundtrip()
		{
			RocketSettingsPacketData data = Snapshot();
			var input = new RocketSettingsStatePacket(data, 41);
			RocketSettingsStatePacket output = Roundtrip(input, new RocketSettingsStatePacket());
			if (output.Revision != 41 || output.Data.TargetLifecycleRevision != 23 ||
			    output.Data.CraftPhase != RocketCraftPhase.InFlight ||
			    output.Data.CraftLocationQ != 7 || output.Data.CraftLocationR != -4 ||
			    output.Data.HasCurrentPad || output.Data.CurrentPadNetId != 0)
				return UnitTestResult.Fail("Rocket authority revision or craft lifecycle state drifted");

			var repair = new RocketSettingsRequestPacket(data, snapshotOnly: true);
			RocketSettingsRequestPacket repairCopy = Roundtrip(
				repair, new RocketSettingsRequestPacket());
			return repairCopy.SnapshotOnly && repairCopy.Data.TargetNetId == 17
				? UnitTestResult.Pass("Revision, location, phase, lifecycle and repair intent roundtrip")
				: UnitTestResult.Fail("Fresh-snapshot request lost its repair intent");
		}

		[UnitTest(name: "Spaced Out rocket authority rejects stale revisions", category: "Sync")]
		public static UnitTestResult StaleRevisionGate()
		{
			if (!RocketSettingsStatePacket.ShouldAcceptRevision(40, 41) ||
			    RocketSettingsStatePacket.ShouldAcceptRevision(41, 41) ||
			    RocketSettingsStatePacket.ShouldAcceptRevision(42, 41) ||
			    RocketSettingsStatePacket.ShouldAcceptRevision(0, 0))
				return UnitTestResult.Fail("Rocket state revision gate is not strictly monotonic");
			return UnitTestResult.Pass("Duplicate and stale host snapshots are rejected");
		}

		[UnitTest(name: "Spaced Out rocket phase and pad invariants are bounded", category: "Sync")]
		public static UnitTestResult PhasePadWireInvariant()
		{
			RocketSettingsPacketData data = Snapshot();
			if (!data.IsWireValid())
				return UnitTestResult.Fail("Valid in-flight snapshot was rejected");

			data.HasCurrentPad = true;
			data.CurrentPadNetId = 29;
			if (data.IsWireValid())
				return UnitTestResult.Fail("In-flight snapshot retained a grounded pad");

			data.CraftPhase = RocketCraftPhase.Landing;
			if (!data.IsWireValid())
				return UnitTestResult.Fail("Landing snapshot with a pad was rejected");

			data.TargetLifecycleRevision = 0;
			return !data.IsWireValid()
				? UnitTestResult.Pass("Impossible phase/pad and zero lifecycle snapshots are rejected")
				: UnitTestResult.Fail("Zero lifecycle revision was accepted");
		}

		[UnitTest(name: "Spaced Out rocket postcondition covers authority domain", category: "Sync")]
		public static UnitTestResult AuthorityPostcondition()
		{
			RocketSettingsPacketData expected = Snapshot();
			RocketSettingsPacketData actual = Snapshot();
			if (!RocketSettingsSync.SnapshotsMatch(expected, actual))
				return UnitTestResult.Fail("Equal authority snapshots did not match");

			actual.CraftLocationQ++;
			if (RocketSettingsSync.SnapshotsMatch(expected, actual))
				return UnitTestResult.Fail("Craft location drift passed postcondition");
			actual = Snapshot();
			actual.CraftPhase = RocketCraftPhase.Landing;
			actual.HasCurrentPad = true;
			actual.CurrentPadNetId = 29;
			if (RocketSettingsSync.SnapshotsMatch(expected, actual))
				return UnitTestResult.Fail("Craft phase drift passed postcondition");
			actual = Snapshot();
			actual.TargetLifecycleRevision++;
			return !RocketSettingsSync.SnapshotsMatch(expected, actual)
				? UnitTestResult.Pass("Location, phase, pad and lifecycle require exact readback")
				: UnitTestResult.Fail("Lifecycle drift passed postcondition");
		}

		private static RocketSettingsPacketData Snapshot()
			=> new()
			{
				TargetKind = RocketSettingsTarget.DestinationSelector,
				TargetNetId = 17,
				TargetLifecycleRevision = 23,
				HasDestination = true,
				DestinationQ = 9,
				DestinationR = -5,
				Repeat = true,
				HasCraftState = true,
				CraftLocationQ = 7,
				CraftLocationR = -4,
				CraftPhase = RocketCraftPhase.InFlight,
				HasCurrentPad = false,
				CurrentPadNetId = 0,
			};

		private static T Roundtrip<T>(T input, T output) where T : IPacket
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				input.Serialize(writer);
			stream.Position = 0;
			using var reader = new BinaryReader(stream);
			output.Deserialize(reader);
			return output;
		}
	}
}
