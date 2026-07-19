using System.IO;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class PlantLifecycleSyncTests
	{
		[UnitTest(name: "Plant lifecycle: packet roundtrip", category: "Networking")]
		public static UnitTestResult PacketRoundtrip()
		{
			PlantData plant = CreatePlantData(41, 17);
			var snapshot = Roundtrip(new PlantGrowthStatePacket
			{
				SnapshotRevision = 23,
				Plants = [plant]
			});
			if (snapshot.SnapshotRevision != 23 || snapshot.Plants.Count != 1
			    || !SamePlantData(snapshot.Plants[0], plant))
				return UnitTestResult.Fail("PlantGrowthStatePacket lost lifecycle metadata");

			var lifecycle = Roundtrip(new PlantLifecyclePacket
			{
				Operation = PlantLifecycleOperation.Spawn,
				Plant = plant
			});
			if (lifecycle.Operation != PlantLifecycleOperation.Spawn
			    || !SamePlantData(lifecycle.Plant, plant))
				return UnitTestResult.Fail("PlantLifecyclePacket lost lifecycle metadata");
			return UnitTestResult.Pass("Plant snapshot and lifecycle payloads roundtrip");
		}

		[UnitTest(name: "Plant lifecycle: stale state loses to tombstone", category: "Networking")]
		public static UnitTestResult StaleStateLosesToTombstone()
		{
			if (PlantGrowthSyncer.ShouldApplyPlantRevision(12, true, 11)
			    || PlantGrowthSyncer.ShouldApplyPlantRevision(12, true, 12))
				return UnitTestResult.Fail("A stale plant state crossed its tombstone");
			if (!PlantGrowthSyncer.ShouldApplyPlantRevision(12, false, 12))
				return UnitTestResult.Fail("Current live plant state was rejected");
			return UnitTestResult.Pass("Tombstones reject stale and duplicate plant state");
		}

		[UnitTest(name: "Plant lifecycle: newer same-NetId replant wins", category: "Networking")]
		public static UnitTestResult NewerSameNetIdReplantWins()
		{
			if (!PlantGrowthSyncer.ShouldApplyPlantRevision(12, true, 13))
				return UnitTestResult.Fail("A newer same-NetId replant could not clear the tombstone");
			if (PlantGrowthSyncer.ShouldApplyPlantRevision(13, false, 12))
				return UnitTestResult.Fail("An older lifecycle replaced a newer replant");
			return UnitTestResult.Pass("Newer lifecycle revision permits same-NetId replant");
		}

		[UnitTest(name: "Plant lifecycle: late remove cannot reopen tombstone", category: "Networking")]
		public static UnitTestResult LateRemoveCannotReopenTombstone()
		{
			if (!PlantGrowthSyncer.ShouldCaptureRemovalRevision(12, false))
				return UnitTestResult.Fail("Live plant removal lost its lifecycle revision");
			if (PlantGrowthSyncer.ShouldCaptureRemovalRevision(13, true))
				return UnitTestResult.Fail("Late plant removal reopened an ended lifecycle");
			return UnitTestResult.Pass("Remove only carries a still-live lifecycle revision");
		}

		[UnitTest(name: "Plant lifecycle: old snapshot preserves new absence", category: "Networking")]
		public static UnitTestResult OldSnapshotPreservesNewAbsence()
		{
			if (PlantGrowthSyncer.ShouldApplySnapshotRevision(20, 20)
			    || PlantGrowthSyncer.ShouldApplySnapshotRevision(20, 19)
			    || !PlantGrowthSyncer.ShouldApplySnapshotRevision(20, 21))
				return UnitTestResult.Fail("Plant snapshot revision gate is not monotonic");
			if (PlantGrowthSyncer.ShouldRemoveAbsentPlant(21, 20)
			    || PlantGrowthSyncer.ShouldRemoveAbsentPlant(20, 20))
				return UnitTestResult.Fail("Old snapshot removed a plant created at or after its cut");
			if (!PlantGrowthSyncer.ShouldRemoveAbsentPlant(19, 20))
				return UnitTestResult.Fail("Current snapshot retained a genuinely absent old plant");
			return UnitTestResult.Pass("Snapshot absence respects the lifecycle cut");
		}

		private static PlantData CreatePlantData(int netId, ulong lifecycleRevision)
		{
			return new PlantData
			{
				PlantNetId = netId,
				LifecycleRevision = lifecycleRevision,
				ReceptacleNetId = 9,
				Cell = 123,
				PlantPrefabTag = "MealLice",
				Maturity = 0.75f,
				IsWilting = true,
				IsHarvestReady = false,
				IsWild = false
			};
		}

		private static bool SamePlantData(PlantData left, PlantData right)
		{
			return left.PlantNetId == right.PlantNetId
			       && left.LifecycleRevision == right.LifecycleRevision
			       && left.ReceptacleNetId == right.ReceptacleNetId
			       && left.Cell == right.Cell
			       && left.PlantPrefabTag == right.PlantPrefabTag
			       && left.Maturity == right.Maturity
			       && left.IsWilting == right.IsWilting
			       && left.IsHarvestReady == right.IsHarvestReady
			       && left.IsWild == right.IsWild;
		}

		private static T Roundtrip<T>(T packet) where T : IPacket, new()
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				packet.Serialize(writer);
			stream.Position = 0;
			var copy = new T();
			using var reader = new BinaryReader(stream);
			copy.Deserialize(reader);
			return copy;
		}
	}
}
