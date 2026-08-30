using System.IO;
using System.Linq;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Events;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.Packets.World.Handlers;
using ONI_Together.Networking.Components;
using ONI_Together.Patches.Critters;
using ONI_Together.Patches.World;
using ONI_Together.Patches.World.Buildings;
using ONI_Together.Patches.OxySync;
using ONI_Together.Networking.OxySync.Components;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class KnownIssueRegressionTests
	{
		[UnitTest(name: "Known issues: Bottle Emptier configuration key", category: "KnownIssues")]
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

		[UnitTest(name: "Known issues: delivered storage FX roundtrip", category: "KnownIssues")]
		public static UnitTestResult DeliveredStorageFxRoundtrip()
		{
			var original = new StorageItemPacket
			{
				FxPrefix = Storage.FXPrefix.Delivered,
				ConsumedPrefabHash = 123,
				ConsumedAmount = 4.5f
			};
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				original.Serialize(writer);
			stream.Position = 0;
			var copy = new StorageItemPacket();
			using (var reader = new BinaryReader(stream))
				copy.Deserialize(reader);

			if (copy.FxPrefix != Storage.FXPrefix.Delivered || copy.ConsumedAmount != original.ConsumedAmount)
				return UnitTestResult.Fail("Delivered storage FX data changed during serialization");
			return UnitTestResult.Pass("Delivered storage FX prefix and amount roundtrip OK");
		}

		[UnitTest(name: "Known issues: client WorldDamage is suppressed", category: "KnownIssues")]
		public static UnitTestResult ClientWorldDamageSuppressed()
		{
			bool oldInSession = MultiplayerSession.InActiveSession;
			bool oldIsHost = MultiplayerSession.IsHost;
			try
			{
				MultiplayerSession.InActiveSession = true;
				MultiplayerSession.IsHost = false;
				bool runOriginal = WorldDamagePatch.Prefix(0, 1f, 293.15f, 0, 0, 0);
				return runOriginal
					? UnitTestResult.Fail("Client would still execute the local WorldDamage spawn path")
					: UnitTestResult.Pass("Client WorldDamage spawn path is suppressed");
			}
			finally
			{
				MultiplayerSession.InActiveSession = oldInSession;
				MultiplayerSession.IsHost = oldIsHost;
			}
		}

		[UnitTest(name: "Known issues: state handler keys", category: "KnownIssues")]
		public static UnitTestResult RuntimeStateHandlerKeys()
		{
			var hashes = new AuthoritativeStateHandler().SupportedConfigHashes;
			if (!hashes.Contains(AuthoritativeStateHandler.HitPointsKey.GetHashCode()) ||
				!hashes.Contains(AuthoritativeStateHandler.EmptyConduitKey.GetHashCode()) ||
				!hashes.Contains(StateMachineStateSyncer.ConfigKey.GetHashCode()))
				return UnitTestResult.Fail("A known-issue runtime state key is not registered");
			return UnitTestResult.Pass("Known-issue runtime state keys are registered");
		}

		[UnitTest(name: "Known issues: null crash guards", category: "KnownIssues")]
		public static UnitTestResult NullCrashGuards()
		{
			bool rocketResult = true;
			bool runRocket = RocketPatches.LaunchableRocketCluster_IsNotGroundBound_Patch.Prefix(null, ref rocketResult);
			bool runTemperature = CreatureTemperaturePatches.CreatureSimTemperatureTransfer_UpdateAverage_Patch.Prefix(null);
			bool fabricatorResult = true;
			bool runFabricator = ComplexFabricator_Patches.ComplexFabricatorSideScreen_HasAllRecipeRequirements_Patch.Prefix(null, null, ref fabricatorResult);

			if (runRocket || rocketResult || runTemperature || runFabricator || fabricatorResult)
				return UnitTestResult.Fail("At least one null crash guard would still run unsafe game code");
			return UnitTestResult.Pass("Rocket, creature-temperature and fabricator null guards are safe");
		}

		[UnitTest(name: "Known issues: status receiver tolerates helper objects", category: "KnownIssues")]
		public static UnitTestResult StatusReceiverHelperObjectGuard()
		{
			var receiver = StatusItemGroupSyncPatch.ResolveReceiverType(null);
			if (receiver != StatusItemsSyncer.StatusRecieverType.MISC)
				return UnitTestResult.Fail("A helper object without KPrefabID was not classified safely");
			return UnitTestResult.Pass("Status receiver classification tolerates objects without KPrefabID");
		}
	}
}
