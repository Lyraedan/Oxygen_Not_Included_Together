using System.IO;
using System.Linq;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class GroundItemTests
	{
		[UnitTest(name: "GroundItemPickedUpPacket: serialization roundtrip", category: "GroundItems")]
		public static UnitTestResult PacketRoundtrip()
		{
			var original = new GroundItemPickedUpPacket { NetId = 999888777 };
			using var ms = new MemoryStream();
			using var writer = new BinaryWriter(ms);
			original.Serialize(writer);
			ms.Position = 0;
			using var reader = new BinaryReader(ms);
			var copy = new GroundItemPickedUpPacket();
			copy.Deserialize(reader);
			if (copy.NetId != original.NetId)
				return UnitTestResult.Fail($"NetId mismatch: {copy.NetId} != {original.NetId}");
			return UnitTestResult.Pass("GroundItemPickedUpPacket roundtrip OK");
		}

		[UnitTest(name: "GroundItemPickedUpPacket: sends immediately", category: "GroundItems")]
		public static UnitTestResult SendsImmediately()
		{
			if (typeof(IBulkablePacket).IsAssignableFrom(typeof(GroundItemPickedUpPacket)))
				return UnitTestResult.Fail("GroundItemPickedUpPacket still depends on bulk flushing");

			return UnitTestResult.Pass("GroundItemPickedUpPacket dispatches immediately and stays independent of bulk flush timing");
		}

		[UnitTest(name: "World damage late spawn respects lifecycle tombstone", category: "GroundItems")]
		public static UnitTestResult LateWorldDamageSpawnRespectsTombstone()
		{
			bool normalSpawn = WorldDamageSpawnResourcePacket.ShouldApply(
				localIsHost: false, senderIsHost: true,
				entityExists: false, lifecycleTombstoned: false);
			bool lateSpawn = WorldDamageSpawnResourcePacket.ShouldApply(
				localIsHost: false, senderIsHost: true,
				entityExists: false, lifecycleTombstoned: true);
			return normalSpawn && !lateSpawn
				? UnitTestResult.Pass("Lifecycle tombstone rejects a late world-damage resource spawn")
				: UnitTestResult.Fail("Lifecycle tombstone allowed a late world-damage resource spawn");
		}

		[UnitTest(name: "GroundItems: NetworkIdentityRegistry accessible", category: "GroundItems")]
		public static UnitTestResult RegistryAccessible()
		{
			// TryGetComponent with a non-existent NetId should return false (not throw)
			bool found = NetworkIdentityRegistry.TryGetComponent<Pickupable>(-1, out _);
			if (found)
				return UnitTestResult.Fail("NetId -1 should not exist in registry");
			return UnitTestResult.Pass("NetworkIdentityRegistry.TryGetComponent accessible and returns false for unknown NetId");
		}

		[UnitTest(name: "ClearTool.Instance accessible (sweep relay)", category: "GroundItems", liveSafe: true)]
		public static UnitTestResult ClearToolAccessible()
		{
			if (ClearTool.Instance == null)
				return Game.Instance == null
					? UnitTestResult.Skip("Requires a loaded colony")
					: UnitTestResult.Fail("ClearTool.Instance is null");
			return UnitTestResult.Pass("ClearTool.Instance accessible");
		}

		[UnitTest(name: "GroundItemPickedUpPacket: pending removal queue", category: "GroundItems")]
		public static UnitTestResult PendingRemovalQueue()
		{
			const int testNetId = -424242;
			NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry[] original =
				NetworkIdentityRegistry.GetLifecycleRevisionSnapshot().ToArray();
			try
			{
				GroundItemPickedUpPacket.ClearPending();
				ulong originalRevision = NetworkIdentityRegistry.GetLastLifecycleRevision(testNetId);
				new GroundItemPickedUpPacket { NetId = testNetId }.OnDispatched();
				if (NetworkIdentityRegistry.GetLastLifecycleRevision(testNetId) != originalRevision)
					return UnitTestResult.Fail("Invalid zero-revision pickup mutated lifecycle state");
				if (GroundItemPickedUpPacket.TryConsumePending(testNetId))
					return UnitTestResult.Fail("Invalid zero-revision pickup queued a removal");

				var packet = new GroundItemPickedUpPacket
				{
					NetId = testNetId,
					Revision = originalRevision + 1
				};
				packet.OnDispatched();

				if (!GroundItemPickedUpPacket.TryConsumePending(testNetId))
					return UnitTestResult.Fail("Expected pending pickup removal to be queued for unresolved NetId");
				if (GroundItemPickedUpPacket.TryConsumePending(testNetId))
					return UnitTestResult.Fail("Pending pickup removal should be consumed only once");

				return UnitTestResult.Pass("Pending pickup removals queue and consume correctly");
			}
			finally
			{
				GroundItemPickedUpPacket.ClearPending();
				NetworkIdentityRegistry.TryReplaceLifecycleRevisionBaseline(original);
			}
		}
	}
}
