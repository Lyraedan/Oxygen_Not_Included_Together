using System.IO;
using System.Linq;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Events;
using ONI_Together.Networking.Packets.World.Handlers;
using ONI_Together.Patches.World;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class KnownIssueRegressionTests
	{
		[UnitTest(name: "Known issues: Bottle Emptier protocol key", category: "KnownIssues")]
		public static UnitTestResult BottleEmptierProtocolKey()
		{
			int expected = "BottleEmptierAllowManualPump".GetHashCode();
			if (!new MiscBuildingHandler().SupportedConfigHashes.Contains(expected))
				return UnitTestResult.Fail("Bottle Emptier receiver does not advertise the sender key");
			return UnitTestResult.Pass("Bottle Emptier sender/receiver key is registered");
		}

		[UnitTest(name: "Known issues: notification packet roundtrip", category: "KnownIssues")]
		public static UnitTestResult NotificationPacketRoundtrip()
		{
			var original = new NotificationPacket { Title = "Alert", Text = "Details", TypeName = "Bad" };
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				original.Serialize(writer);
			stream.Position = 0;
			var copy = new NotificationPacket();
			using (var reader = new BinaryReader(stream))
				copy.Deserialize(reader);

			if (copy.Title != original.Title || copy.Text != original.Text || copy.TypeName != original.TypeName)
				return UnitTestResult.Fail("Notification payload changed during serialization");
			return UnitTestResult.Pass("Notification payload roundtrip OK");
		}

		[UnitTest(name: "Known issues: client WorldDamage is suppressed", category: "KnownIssues")]
		public static UnitTestResult ClientWorldDamageSuppressed()
		{
			bool oldInSession = MultiplayerSession.InSession;
			bool oldIsHost = MultiplayerSession.IsHost;
			try
			{
				MultiplayerSession.InSession = true;
				MultiplayerSession.IsHost = false;
				bool runOriginal = WorldDamagePatch.Prefix(0, 1f, 293.15f, 0, 0, 0);
				return runOriginal
					? UnitTestResult.Fail("Client would still execute the local WorldDamage spawn path")
					: UnitTestResult.Pass("Client WorldDamage spawn path is suppressed");
			}
			finally
			{
				MultiplayerSession.InSession = oldInSession;
				MultiplayerSession.IsHost = oldIsHost;
			}
		}

		[UnitTest(name: "Known issues: state handler keys", category: "KnownIssues")]
		public static UnitTestResult RuntimeStateHandlerKeys()
		{
			var hashes = new KnownIssueStateHandler().SupportedConfigHashes;
			if (!hashes.Contains(KnownIssueStateHandler.HitPointsKey.GetHashCode()) ||
				!hashes.Contains(KnownIssueStateHandler.EmptyConduitKey.GetHashCode()))
				return UnitTestResult.Fail("A known-issue runtime state key is not registered");
			return UnitTestResult.Pass("Known-issue runtime state keys are registered");
		}
	}
}
