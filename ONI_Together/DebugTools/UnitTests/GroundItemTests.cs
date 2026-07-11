using System.IO;
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

		[UnitTest(name: "GroundItems: NetworkIdentityRegistry accessible", category: "GroundItems")]
		public static UnitTestResult RegistryAccessible()
		{
			// TryGetComponent with a non-existent NetId should return false (not throw)
			bool found = NetworkIdentityRegistry.TryGetComponent<Pickupable>(-1, out _);
			if (found)
				return UnitTestResult.Fail("NetId -1 should not exist in registry");
			return UnitTestResult.Pass("NetworkIdentityRegistry.TryGetComponent accessible and returns false for unknown NetId");
		}

		[UnitTest(name: "ClearTool.Instance accessible (sweep relay)", category: "GroundItems")]
		public static UnitTestResult ClearToolAccessible()
		{
			if (ClearTool.Instance == null)
				return UnitTestResult.Fail("ClearTool.Instance is null");
			return UnitTestResult.Pass("ClearTool.Instance accessible");
		}

		[UnitTest(name: "GroundItemPickedUpPacket: pending removal queue", category: "GroundItems")]
		public static UnitTestResult PendingRemovalQueue()
		{
			const int testNetId = 424242;
			PendingPickupRegistry.Clear();

			var packet = new GroundItemPickedUpPacket { NetId = testNetId };
			packet.OnDispatched();

			if (!PendingPickupRegistry.TryConsume(testNetId))
				return UnitTestResult.Fail("Expected pending pickup removal to be queued for unresolved NetId");

			if (PendingPickupRegistry.TryConsume(testNetId))
				return UnitTestResult.Fail("Pending pickup removal should be consumed only once");

			PendingPickupRegistry.Clear();
			return UnitTestResult.Pass("Pending pickup removals queue and consume correctly");
		}
	}
}
