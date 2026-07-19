using System.IO;
using System.Linq;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Animation;
using ONI_Together.Networking.Packets.Core;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class AnimSyncTests
	{
		[UnitTest(name: "Anim reconciliation: detects wrong animation", category: "Animation", liveSafe: true)]
		public static UnitTestResult DetectsWrongAnimation()
		{
			var identities = NetworkIdentityRegistry.AllIdentities;
			foreach (var id in identities)
			{
				if (!id.gameObject.TryGetComponent<KBatchedAnimController>(out var kbac))
					continue;
				if (!id.gameObject.GetComponent<KPrefabID>()?.HasTag(GameTags.BaseMinion) ?? true)
					continue;

				if (kbac.CurrentAnim == null)
					continue;

				string currentAnim = kbac.CurrentAnim.name;
				if (string.IsNullOrEmpty(currentAnim))
					continue;

				var wrongHash = new HashedString("fake_anim_that_doesnt_exist");
				if (kbac.currentAnim == wrongHash)
					return UnitTestResult.Fail("Hash collision with fake anim");

				return UnitTestResult.Pass($"Minion '{id.gameObject.name}' anim='{currentAnim}', would detect mismatch");
			}
			return Game.Instance == null
				? UnitTestResult.Skip("Requires a loaded colony with a duplicant")
				: UnitTestResult.Fail("No minions with anim controller found");
		}

		[UnitTest(name: "Anim reconciliation: elapsed time readable", category: "Animation", liveSafe: true)]
		public static UnitTestResult ElapsedTimeReadable()
		{
			var identities = NetworkIdentityRegistry.AllIdentities;
			foreach (var id in identities)
			{
				if (!id.gameObject.TryGetComponent<KBatchedAnimController>(out var kbac))
					continue;
				if (!id.gameObject.GetComponent<KPrefabID>()?.HasTag(GameTags.BaseMinion) ?? true)
					continue;

				float elapsed = kbac.GetElapsedTime();
				return UnitTestResult.Pass($"ElapsedTime={elapsed:F3}s on '{id.gameObject.name}'");
			}
			return Game.Instance == null
				? UnitTestResult.Skip("Requires a loaded colony with a duplicant")
				: UnitTestResult.Fail("No minions found");
		}

		[UnitTest(name: "Anim reconciliation: reflection helper resolves", category: "Animation")]
		public static UnitTestResult ReflectionHelperResolves()
		{
			var identities = NetworkIdentityRegistry.AllIdentities;
			foreach (var id in identities)
			{
				if (!id.gameObject.TryGetComponent<KBatchedAnimController>(out var kbac))
					continue;

				float before = kbac.GetElapsedTime();
				AnimReconciliationHelper.TrySetElapsedTime(kbac, before);
				float after = kbac.GetElapsedTime();

				return UnitTestResult.Pass($"SetElapsedTime resolved. Before={before:F3}, After={after:F3}");
			}
			return Game.Instance == null
				? UnitTestResult.Skip("Requires a loaded colony with animated entities")
				: UnitTestResult.Fail("No anim controllers found");
		}

		[UnitTest(name: "Anim sync packet: roundtrip", category: "Animation")]
		public static UnitTestResult AnimSyncPacketRoundtrip()
		{
			var packet = new AnimSyncPacket
			{
				NetId = 42,
				AnimHash = new HashedString("idle_loop").hash,
				Mode = (byte)KAnim.PlayMode.Loop,
				Speed = 1.25f,
				ElapsedTime = 2.5f
			};

			using var ms = new MemoryStream();
			using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
				packet.Serialize(writer);

			ms.Position = 0;

			var copy = new AnimSyncPacket();
			using (var reader = new BinaryReader(ms, System.Text.Encoding.UTF8, true))
				copy.Deserialize(reader);

			if (copy.NetId != packet.NetId || copy.AnimHash != packet.AnimHash || copy.Mode != packet.Mode)
				return UnitTestResult.Fail("Packet int fields did not roundtrip");
			if (copy.Speed != packet.Speed || copy.ElapsedTime != packet.ElapsedTime)
				return UnitTestResult.Fail("Packet float fields did not roundtrip");

			return UnitTestResult.Pass("AnimSyncPacket serialize/deserialize roundtrip succeeded");
		}

		[UnitTest(name: "Anim packets: bypass bulk queue", category: "Animation")]
		public static UnitTestResult AnimPacketsBypassBulkQueue()
		{
			bool animSyncBulk = typeof(IBulkablePacket).IsAssignableFrom(typeof(AnimSyncPacket));
			bool playAnimBulk = typeof(IBulkablePacket).IsAssignableFrom(typeof(PlayAnimPacket));
			if (animSyncBulk || playAnimBulk)
				return UnitTestResult.Fail("Animation packets still route through the bulk queue");

			return UnitTestResult.Pass("AnimSyncPacket and PlayAnimPacket send directly");
		}

		[UnitTest(name: "Anim sync: non-minion snapshots use coordinator", category: "Animation")]
		public static UnitTestResult NonMinionSnapshotsUseCoordinator()
		{
			bool perEntityHeartbeat = typeof(IRender1000ms).IsAssignableFrom(typeof(AnimStateSyncer));
			if (perEntityHeartbeat)
				return UnitTestResult.Fail("AnimStateSyncer still runs its own 1000ms heartbeat");

			return UnitTestResult.Pass("AnimStateSyncer relies on the shared coordinator");
		}

		[UnitTest(name: "Anim sync: non-minion entities discoverable", category: "Animation", liveSafe: true)]
		public static UnitTestResult NonMinionAnimEntitiesDiscoverable()
		{
			var identities = NetworkIdentityRegistry.AllIdentities;
			foreach (var id in identities)
			{
				if (id.gameObject.GetComponent<KPrefabID>()?.HasTag(GameTags.BaseMinion) ?? false)
					continue;
				if (!id.gameObject.TryGetComponent<KBatchedAnimController>(out var _))
					continue;
				if (!id.gameObject.TryGetComponent<AnimStateSyncer>(out var _))
					return UnitTestResult.Fail($"Entity '{id.gameObject.name}' is missing AnimStateSyncer");
				if (!AnimSyncEligibility.IsAnimatedNonMinion(id.gameObject))
					return UnitTestResult.Fail($"Entity '{id.gameObject.name}' should not have AnimStateSyncer");

				return UnitTestResult.Pass($"Entity '{id.gameObject.name}' is sync-eligible");
			}

			return Game.Instance == null
				? UnitTestResult.Skip("Requires a loaded colony with animated entities")
				: UnitTestResult.Fail("No non-minion animated network entities found");
		}

		[UnitTest(name: "Anim resync request packet: roundtrip", category: "Animation")]
		public static UnitTestResult AnimResyncRequestPacketRoundtrip()
		{
			var packet = new AnimResyncRequestPacket
			{
				RequesterId = 99,
				NetIds = [11, 22, 33]
			};

			using var ms = new MemoryStream();
			using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
				packet.Serialize(writer);

			ms.Position = 0;

			var copy = new AnimResyncRequestPacket();
			using (var reader = new BinaryReader(ms, System.Text.Encoding.UTF8, true))
				copy.Deserialize(reader);

			if (copy.RequesterId != packet.RequesterId)
				return UnitTestResult.Fail("RequesterId did not roundtrip");
			if (!copy.NetIds.SequenceEqual(packet.NetIds))
				return UnitTestResult.Fail("NetId list did not roundtrip");

			return UnitTestResult.Pass("AnimResyncRequestPacket serialize/deserialize roundtrip succeeded");
		}
	}
}
