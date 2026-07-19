#if DEBUG
using System;
using System.Linq;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SoakDomainHashTests
	{
		[UnitTest(name: "Soak hashes separate lifecycle, world, storage, and rocket domains", category: "Networking")]
		public static UnitTestResult DomainHashesDetectIndependentChanges()
		{
			SoakStateHashes baseline = Compute(
				new SoakEntityState { NetId = 7, PrefabHash = 11, Active = true, Revision = 3 },
				new SoakWorldMembershipState
				{
					NetId = 7, WorldId = 2, Cell = 91,
					PositionX = 999, PositionY = -999, PositionZ = 42,
					HasPositionHandler = true, FlipX = true, NavType = NavType.Floor,
				},
				new SoakStorageMembershipState { StorageNetId = 4, ItemNetId = 7 },
				new SoakClusterRocketState
				{
					NetId = 7, ClusterQ = 1, ClusterR = -2, HasDestination = true,
					DestinationQ = 3, DestinationR = 4, PadNetId = 8, Repeat = true
				});

			SoakStateHashes lifecycle = Compute(
				new SoakEntityState { NetId = 7, PrefabHash = 11, Active = true, Revision = 4 },
				new SoakWorldMembershipState { NetId = 7, WorldId = 2, Cell = 91 },
				new SoakStorageMembershipState { StorageNetId = 4, ItemNetId = 7 },
				new SoakClusterRocketState
				{
					NetId = 7, ClusterQ = 1, ClusterR = -2, HasDestination = true,
					DestinationQ = 3, DestinationR = 4, PadNetId = 8, Repeat = true
				});
			SoakStateHashes world = Compute(
				new SoakEntityState { NetId = 7, PrefabHash = 11, Active = true, Revision = 3 },
				new SoakWorldMembershipState { NetId = 7, WorldId = 5, Cell = 91 },
				new SoakStorageMembershipState { StorageNetId = 4, ItemNetId = 7 },
				new SoakClusterRocketState
				{
					NetId = 7, ClusterQ = 1, ClusterR = -2, HasDestination = true,
					DestinationQ = 3, DestinationR = 4, PadNetId = 8, Repeat = true
				});
			SoakStateHashes storage = Compute(
				new SoakEntityState { NetId = 7, PrefabHash = 11, Active = true, Revision = 3 },
				new SoakWorldMembershipState { NetId = 7, WorldId = 2, Cell = 91 },
				new SoakStorageMembershipState { StorageNetId = 9, ItemNetId = 7 },
				new SoakClusterRocketState
				{
					NetId = 7, ClusterQ = 1, ClusterR = -2, HasDestination = true,
					DestinationQ = 3, DestinationR = 4, PadNetId = 8, Repeat = true
				});
			SoakStateHashes storageIndex = Compute(
				new SoakEntityState { NetId = 7, PrefabHash = 11, Active = true, Revision = 3 },
				new SoakWorldMembershipState { NetId = 7, WorldId = 2, Cell = 91 },
				new SoakStorageMembershipState
				{
					StorageNetId = 4, StorageIndex = 1, ItemNetId = 7
				},
				new SoakClusterRocketState
				{
					NetId = 7, ClusterQ = 1, ClusterR = -2, HasDestination = true,
					DestinationQ = 3, DestinationR = 4, PadNetId = 8, Repeat = true
				});
			SoakStateHashes storageBacklink = Compute(
				new SoakEntityState { NetId = 7, PrefabHash = 11, Active = true, Revision = 3 },
				new SoakWorldMembershipState { NetId = 7, WorldId = 2, Cell = 91 },
				new SoakStorageMembershipState
				{
					StorageNetId = 4, ItemNetId = 7, LinkedStorageNetId = 9
				},
				new SoakClusterRocketState
				{
					NetId = 7, ClusterQ = 1, ClusterR = -2, HasDestination = true,
					DestinationQ = 3, DestinationR = 4, PadNetId = 8, Repeat = true
				});
			SoakStateHashes rocket = Compute(
				new SoakEntityState { NetId = 7, PrefabHash = 11, Active = true, Revision = 3 },
				new SoakWorldMembershipState { NetId = 7, WorldId = 2, Cell = 91 },
				new SoakStorageMembershipState { StorageNetId = 4, ItemNetId = 7 },
				new SoakClusterRocketState
				{
					NetId = 7, ClusterQ = 6, ClusterR = -2, HasDestination = true,
					DestinationQ = 3, DestinationR = 4, PadNetId = 8, Repeat = true
				});

			if (baseline.EntityLifecycle.SequenceEqual(lifecycle.EntityLifecycle))
				return UnitTestResult.Fail("Lifecycle hash ignored revision");
			if (baseline.WorldMembership.SequenceEqual(world.WorldMembership))
				return UnitTestResult.Fail("World hash ignored world membership");
			if (baseline.StorageMembership.SequenceEqual(storage.StorageMembership))
				return UnitTestResult.Fail("Storage hash ignored container membership");
			if (baseline.StorageMembership.SequenceEqual(storageIndex.StorageMembership))
				return UnitTestResult.Fail("Storage hash ignored the storage component index");
			if (baseline.StorageMembership.SequenceEqual(storageBacklink.StorageMembership))
				return UnitTestResult.Fail("Storage hash ignored the pickupable reverse owner");
			if (baseline.ClusterRocket.SequenceEqual(rocket.ClusterRocket))
				return UnitTestResult.Fail("Rocket hash ignored cluster location");
			SoakStateHashes rocketLifecycle = Compute(
				new SoakEntityState { NetId = 7, PrefabHash = 11, Active = true, Revision = 3 },
				new SoakWorldMembershipState { NetId = 7, WorldId = 2, Cell = 91 },
				new SoakStorageMembershipState { StorageNetId = 4, ItemNetId = 7 },
				new SoakClusterRocketState
				{
					NetId = 7, ClusterQ = 1, ClusterR = -2, HasDestination = true,
					DestinationQ = 3, DestinationR = 4, PadNetId = 8, Repeat = true,
					HasCraftState = true, CraftLocationQ = 5, CraftLocationR = -6,
					CraftPhase = Networking.Packets.DLC.SpacedOut.RocketCraftPhase.InFlight,
				});
			if (baseline.ClusterRocket.SequenceEqual(rocketLifecycle.ClusterRocket))
				return UnitTestResult.Fail("Rocket hash ignored craft location or lifecycle phase");
			SoakStateHashes sameCell = Compute(
				new SoakEntityState { NetId = 7, PrefabHash = 11, Active = true, Revision = 3 },
				new SoakWorldMembershipState { NetId = 7, WorldId = 2, Cell = 91 },
				new SoakStorageMembershipState { StorageNetId = 4, ItemNetId = 7 },
				new SoakClusterRocketState
				{
					NetId = 7, ClusterQ = 1, ClusterR = -2, HasDestination = true,
					DestinationQ = 3, DestinationR = 4, PadNetId = 8, Repeat = true
				});
			if (baseline.WorldMembership.SequenceEqual(sameCell.WorldMembership))
				return UnitTestResult.Fail("World hash ignored exact authoritative position");
			SoakStateHashes navigation = Compute(
				new SoakEntityState { NetId = 7, PrefabHash = 11, Active = true, Revision = 3 },
				new SoakWorldMembershipState
				{
					NetId = 7, WorldId = 2, Cell = 91,
					PositionX = 999, PositionY = -999, PositionZ = 42,
					HasPositionHandler = true, FlipX = true, NavType = NavType.Ladder,
				},
				new SoakStorageMembershipState { StorageNetId = 4, ItemNetId = 7 },
				new SoakClusterRocketState
				{
					NetId = 7, ClusterQ = 1, ClusterR = -2, HasDestination = true,
					DestinationQ = 3, DestinationR = 4, PadNetId = 8, Repeat = true
				});
			if (baseline.WorldMembership.SequenceEqual(navigation.WorldMembership))
				return UnitTestResult.Fail("World hash ignored authoritative flip or navigation state");
			return UnitTestResult.Pass("Each authoritative soak domain detects its own state changes");
		}

		[UnitTest(name: "Soak storage hash preserves item order", category: "Networking")]
		public static UnitTestResult StorageHashPreservesItemOrder()
		{
			SoakStateHashes ordered = ComputeStorageOrder(10, 20);
			SoakStateHashes reversed = ComputeStorageOrder(20, 10);
			return ordered.StorageMembership.SequenceEqual(reversed.StorageMembership)
				? UnitTestResult.Fail("Storage hash ignored deterministic item order")
				: UnitTestResult.Pass("Storage item order participates in the hash");
		}

		private static SoakStateHashes ComputeStorageOrder(int firstNetId, int secondNetId)
		{
			return SoakStateHash.Compute(
				Enumerable.Empty<SoakCellState>(),
				Enumerable.Empty<SoakEntityState>(),
				Enumerable.Empty<SoakWorldMembershipState>(),
				new[]
				{
					new SoakStorageMembershipState
						{ StorageNetId = 4, HasItem = true, ItemIndex = 0, ItemNetId = firstNetId },
					new SoakStorageMembershipState
						{ StorageNetId = 4, HasItem = true, ItemIndex = 1, ItemNetId = secondNetId },
				},
				Enumerable.Empty<SoakClusterRocketState>());
		}

		private static SoakStateHashes Compute(
			SoakEntityState entity,
			SoakWorldMembershipState world,
			SoakStorageMembershipState storage,
			SoakClusterRocketState rocket)
			=> SoakStateHash.Compute(
				Enumerable.Empty<SoakCellState>(),
				new[] { entity },
				new[] { world },
				new[] { storage },
				new[] { rocket });
	}
}
#endif
