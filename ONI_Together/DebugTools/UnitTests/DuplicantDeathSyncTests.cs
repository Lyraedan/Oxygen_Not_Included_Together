#if DEBUG
using System;
using System.IO;
using System.Linq;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.Packets.World.Buildings;
using ONI_Together.Networking.Synchronization;
using ONI_Together.Patches.World.Buildings;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class DuplicantDeathSyncTests
	{
		[UnitTest(name: "Duplicant death packet enforces authority and wire bounds", category: "Networking")]
		public static UnitTestResult DeathPacketWireSafety()
		{
			var source = new DuplicantDeathStatePacket
			{
				NetId = 17,
				LifecycleRevision = 19,
				Revision = 23,
				DeathId = "Suffocation"
			};
			DuplicantDeathStatePacket copy = RoundTrip(source);
			if (copy.NetId != source.NetId
			    || copy.LifecycleRevision != source.LifecycleRevision
			    || copy.Revision != source.Revision
			    || copy.DeathId != source.DeathId)
				return UnitTestResult.Fail("Death packet did not round-trip its authoritative state");
			if (!DuplicantDeathStatePacket.ShouldApply(false, true)
			    || DuplicantDeathStatePacket.ShouldApply(true, true)
			    || DuplicantDeathStatePacket.ShouldApply(false, false))
				return UnitTestResult.Fail("Death packet authority gate is incorrect");
			if (!ThrowsInvalidData(new DuplicantDeathStatePacket
			    { NetId = 0, LifecycleRevision = 1, Revision = 2, DeathId = "Generic" }))
				return UnitTestResult.Fail("Death packet accepted an invalid NetId");
			if (!ThrowsInvalidData(new DuplicantDeathStatePacket
			    {
				    NetId = 1,
				    LifecycleRevision = 2,
				    Revision = 3,
				    DeathId = new string('x', DuplicantDeathSync.MaxDeathIdLength + 1)
			    }))
				return UnitTestResult.Fail("Death packet accepted an oversized death id");
			if (!RejectsDeclaredDeathByteLength(DuplicantDeathWire.MaxDeathIdBytes + 1))
				return UnitTestResult.Fail("Death packet allocated an oversized declared death id");
			return UnitTestResult.Pass("Death packet wire and authority checks are enforced");
		}

		[UnitTest(name: "Duplicant death pending is lifecycle-bound and bounded", category: "Networking")]
		public static UnitTestResult DeathPendingLifecycleSafety()
		{
			DuplicantDeathStatePacket.ClearState();
			DuplicantDeathStatePacket.StorePendingForTests(DeathPacket(7, 10, 20), 0f);
			DuplicantDeathStatePacket.StorePendingForTests(DeathPacket(7, 9, 21), 0f);
			if (!DuplicantDeathStatePacket.HasPendingForTests(7, 10)
			    || DuplicantDeathStatePacket.HasPendingForTests(7, 9))
				return UnitTestResult.Fail("An older lifecycle replaced the pending death");
			DuplicantDeathStatePacket.StorePendingForTests(DeathPacket(7, 11, 22), 0f);
			if (DuplicantDeathStatePacket.HasPendingForTests(7, 10)
			    || !DuplicantDeathStatePacket.HasPendingForTests(7, 11))
				return UnitTestResult.Fail("A new lifecycle did not retire the old pending death");
			DuplicantDeathStatePacket.CancelPending(7);
			if (DuplicantDeathStatePacket.PendingCountForTests != 0)
				return UnitTestResult.Fail("Despawn cleanup retained pending death state");
			DuplicantDeathStatePacket.StorePendingForTests(DeathPacket(8, 12, 23), 0f);
			DuplicantDeathStatePacket.PrunePendingForTests(
				DuplicantDeathStatePacket.PendingLifetimeSeconds + 1f);
			if (DuplicantDeathStatePacket.PendingCountForTests != 0)
				return UnitTestResult.Fail("Expired pending death state was retained");
			for (int index = 1; index <= DuplicantDeathStatePacket.MaxPendingDeaths + 1; index++)
				DuplicantDeathStatePacket.StorePendingForTests(
					DeathPacket(index, 100, (ulong)(200 + index)), 0f);
			bool bounded = DuplicantDeathStatePacket.PendingCountForTests
			               <= DuplicantDeathStatePacket.MaxPendingDeaths;
			DuplicantDeathStatePacket.ClearState();
			return bounded
				? UnitTestResult.Pass("Pending death state is lifecycle-bound, expiring, and bounded")
				: UnitTestResult.Fail("Pending death state exceeded its hard cap");
		}

		[UnitTest(name: "Duplicant death rejects stale lifecycle reuse", category: "Networking")]
		public static UnitTestResult DeathLifecycleOrdering()
		{
			bool current = DuplicantDeathStatePacket.IsCurrentLifecycle(8, false, 8);
			bool tombstone = DuplicantDeathStatePacket.IsStaleLifecycle(8, true, 8);
			bool reused = DuplicantDeathStatePacket.IsStaleLifecycle(9, false, 8);
			bool future = DuplicantDeathStatePacket.IsStaleLifecycle(7, false, 8);
			return current && tombstone && reused && !future
				? UnitTestResult.Pass("Death state is accepted only by its exact live lifecycle")
				: UnitTestResult.Fail("Death lifecycle ordering accepted stale reuse");
		}

		[UnitTest(name: "Pending spawn binding is lifecycle-bound and bounded", category: "Networking")]
		public static UnitTestResult PendingSpawnBindingSafety()
		{
			SpawnPrefabPacket.ClearPendingBindings();
			SpawnPrefabPacket.StorePendingBindingForTests(SpawnPacket(7, 10), 0f);
			SpawnPrefabPacket.StorePendingBindingForTests(SpawnPacket(7, 9), 0f);
			if (!SpawnPrefabPacket.HasPendingBindingForTests(7, 10)
			    || SpawnPrefabPacket.HasPendingBindingForTests(7, 9))
				return UnitTestResult.Fail("Older lifecycle replaced a pending spawn binding");
			SpawnPrefabPacket.StorePendingBindingForTests(SpawnPacket(7, 11), 0f);
			if (SpawnPrefabPacket.HasPendingBindingForTests(7, 10)
			    || !SpawnPrefabPacket.HasPendingBindingForTests(7, 11))
				return UnitTestResult.Fail("New lifecycle retained an old pending spawn binding");
			SpawnPrefabPacket.CancelPendingBinding(7);
			if (SpawnPrefabPacket.PendingBindingCountForTests != 0)
				return UnitTestResult.Fail("Despawn retained a pending spawn binding");
			SpawnPrefabPacket.StorePendingBindingForTests(SpawnPacket(8, 12), 0f);
			SpawnPrefabPacket.PrunePendingBindingsForTests(
				SpawnPrefabPacket.PendingBindingLifetimeSeconds + 1f);
			if (SpawnPrefabPacket.PendingBindingCountForTests != 0)
				return UnitTestResult.Fail("Expired pending spawn binding was retained");
			for (int index = 1; index <= SpawnPrefabPacket.MaxPendingBindings + 1; index++)
				SpawnPrefabPacket.StorePendingBindingForTests(
					SpawnPacket(index, (ulong)(100 + index)), 0f);
			bool bounded = SpawnPrefabPacket.PendingBindingCountForTests
			               <= SpawnPrefabPacket.MaxPendingBindings;
			SpawnPrefabPacket.ClearPendingBindings();
			return bounded
				? UnitTestResult.Pass("Pending spawn bindings are lifecycle-bound, expiring, and bounded")
				: UnitTestResult.Fail("Pending spawn bindings exceeded their hard cap");
		}

		[UnitTest(name: "Spawn lifecycle carries duplicant death state", category: "Networking")]
		public static UnitTestResult SpawnLifecycleDeathRoundTrip()
		{
			var source = new SpawnPrefabPacket
			{
				NetId = 9,
				Revision = 10,
				Hash = 11,
				Position = new Vector3(1f, 2f, 3f),
				HasDuplicantState = true,
				IsDuplicantDead = true,
				DuplicantDeathId = "Starvation",
				HasOperationalState = true,
				OperationalIsActive = true,
				OperationalIsFunctional = false,
				OperationalIsOperational = true
			};
			SpawnPrefabPacket copy = RoundTrip(source);
			if (!copy.HasDuplicantState || !copy.IsDuplicantDead
			    || copy.DuplicantDeathId != source.DuplicantDeathId
			    || !copy.HasOperationalState || !copy.OperationalIsActive
			    || copy.OperationalIsFunctional || !copy.OperationalIsOperational)
				return UnitTestResult.Fail("Spawn lifecycle dropped duplicant death state");
			source.IsDuplicantDead = false;
			if (!ThrowsInvalidData(source))
				return UnitTestResult.Fail("Live duplicant snapshot accepted a death id");
			return UnitTestResult.Pass("Spawn lifecycle preserves canonical duplicant death state");
		}

		[UnitTest(name: "Soak entity hash includes duplicant life state", category: "Networking")]
		public static UnitTestResult EntityHashIncludesDuplicantLifeState()
		{
			SoakStateHashes alive = ComputeEntityHash(false, false, true, true, string.Empty, false, false);
			SoakStateHashes dead = ComputeEntityHash(true, true, false, false, "Suffocation", true, true);
			SoakStateHashes brokenRoster = ComputeEntityHash(false, false, false, true, string.Empty, false, false);
			SoakStateHashes brokenModelRoster = ComputeEntityHash(false, false, true, false, string.Empty, false, false);
			SoakStateHashes missingCorpse = ComputeEntityHash(true, false, false, false, "Suffocation", true, true);
			SoakStateHashes otherDeath = ComputeEntityHash(true, true, false, false, "Starvation", true, true);
			SoakStateHashes missingDeadTag = ComputeEntityHash(true, true, false, false, "Suffocation", false, true);
			SoakStateHashes monitorAlive = ComputeEntityHash(true, true, false, false, "Suffocation", true, false);
			if (alive.EntityLifecycle.SequenceEqual(dead.EntityLifecycle))
				return UnitTestResult.Fail("Entity hash ignored duplicant death");
			if (alive.EntityLifecycle.SequenceEqual(brokenRoster.EntityLifecycle))
				return UnitTestResult.Fail("Entity hash ignored live-roster membership");
			if (alive.EntityLifecycle.SequenceEqual(brokenModelRoster.EntityLifecycle))
				return UnitTestResult.Fail("Entity hash ignored per-model live-roster membership");
			if (dead.EntityLifecycle.SequenceEqual(missingCorpse.EntityLifecycle))
				return UnitTestResult.Fail("Entity hash ignored the Corpse tag");
			if (dead.EntityLifecycle.SequenceEqual(otherDeath.EntityLifecycle))
				return UnitTestResult.Fail("Entity hash ignored the canonical death id");
			if (dead.EntityLifecycle.SequenceEqual(missingDeadTag.EntityLifecycle)
			    || dead.EntityLifecycle.SequenceEqual(monitorAlive.EntityLifecycle))
				return UnitTestResult.Fail("Entity hash ignored death tag/monitor invariant mismatch");
			if (!DuplicantDeathSync.IsDeadState(false, true)
			    || !DuplicantDeathSync.IsDeadState(true, false)
			    || DuplicantDeathSync.IsDeadState(false, false))
				return UnitTestResult.Fail("Canonical duplicant death predicate is inconsistent");
			return UnitTestResult.Pass("Duplicant death semantics participate in entity hash");
		}

		[UnitTest(name: "Duplicant snapshot requires exact death invariants", category: "Networking")]
		public static UnitTestResult DeathSnapshotRequiresExactInvariants()
		{
			bool alive = DuplicantDeathSync.SnapshotFieldsMatch(
				false, false, false, false, true, true);
			bool dead = DuplicantDeathSync.SnapshotFieldsMatch(
				true, true, true, true, false, false);
			bool staleMonitor = DuplicantDeathSync.SnapshotFieldsMatch(
				false, false, true, false, true, true);
			bool staleTag = DuplicantDeathSync.SnapshotFieldsMatch(
				false, true, false, false, true, true);
			bool staleCorpse = DuplicantDeathSync.SnapshotFieldsMatch(
				false, false, false, true, true, true);
			bool staleDeathIdCanonicalized = string.IsNullOrEmpty(
				DuplicantDeathSync.CanonicalDeathId(false, "Suffocation"));
			bool deadDeathIdPreserved = DuplicantDeathSync.CanonicalDeathId(
				true, "Suffocation") == "Suffocation";
			bool knownDeathMatches = DuplicantDeathSync.ResolvedDeathMatches(
				"Suffocation", "Suffocation");
			bool unknownDeathRejected = !DuplicantDeathSync.ResolvedDeathMatches(
				"MissingDlcDeath", null);
			return alive && dead && !staleMonitor && !staleTag && !staleCorpse
			       && staleDeathIdCanonicalized && deadDeathIdPreserved
			       && knownDeathMatches && unknownDeathRejected
				? UnitTestResult.Pass("Death snapshots reject every split-brain life state")
				: UnitTestResult.Fail("Death snapshot accepted an inconsistent life state");
		}

		[UnitTest(name: "Operational state rejects stale lifecycle", category: "Networking")]
		public static UnitTestResult OperationalLifecycleWireSafety()
		{
			var source = new OperationalStatePacket
			{
				NetId = 4,
				LifecycleRevision = 5,
				Revision = 6,
				IsActive = true,
				IsFunctional = false,
				IsOperational = true
			};
			OperationalStatePacket copy = RoundTrip(source);
			if (copy.NetId != source.NetId
			    || copy.LifecycleRevision != source.LifecycleRevision
			    || copy.Revision != source.Revision)
				return UnitTestResult.Fail("Operational state dropped lifecycle metadata");
			bool exact = OperationalStatePacket.ShouldApplyLifecycle(5, false, 5);
			bool reused = OperationalStatePacket.ShouldApplyLifecycle(6, false, 5);
			bool tombstoned = OperationalStatePacket.ShouldApplyLifecycle(5, true, 5);
			return exact && !reused && !tombstoned
				? UnitTestResult.Pass("Operational state requires its exact live lifecycle")
				: UnitTestResult.Fail("Operational state accepted stale lifecycle reuse");
		}

		[UnitTest(name: "Failed persistent spawn materialization is queued", category: "Networking")]
		public static UnitTestResult OccupiedMaterializationFailurePolicy()
		{
			return SpawnPrefabPacket.ShouldQueueFailedOccupiedMaterialization(true)
			       && !SpawnPrefabPacket.ShouldQueueFailedOccupiedMaterialization(false)
				? UnitTestResult.Pass("Persistent occupied failures are queued instead of swallowed")
				: UnitTestResult.Fail("Occupied materialization failure policy is incorrect");
		}

		[UnitTest(name: "Destroyed client operational getter is short-circuited", category: "Networking")]
		public static UnitTestResult DestroyedOperationalGuard()
		{
			bool guarded = Operational_Patch.ShouldShortCircuitDestroyedClient(true, true);
			bool live = Operational_Patch.ShouldShortCircuitDestroyedClient(true, false);
			bool host = Operational_Patch.ShouldShortCircuitDestroyedClient(false, true);
			return guarded && !live && !host
				? UnitTestResult.Pass("Destroyed client components are short-circuited")
				: UnitTestResult.Fail("Operational destroyed-component guard is incorrect");
		}

		private static SoakStateHashes ComputeEntityHash(
			bool dead, bool corpse, bool liveRoster, bool modelRoster, string deathId,
			bool hasDeadTag, bool monitorIsDead)
			=> SoakStateHash.Compute(
				Array.Empty<SoakCellState>(),
				new[]
				{
					new SoakEntityState
					{
						NetId = 1,
						PrefabHash = 2,
						Active = true,
						Revision = 3,
						IsDuplicant = true,
						IsDead = dead,
						HasDeadTag = hasDeadTag,
						MonitorIsDead = monitorIsDead,
						IsCorpse = corpse,
						IsInLiveRoster = liveRoster,
						IsInLiveRosterByModel = modelRoster,
						DeathId = deathId
					}
				});

		private static SpawnPrefabPacket SpawnPacket(int netId, ulong revision)
			=> new()
			{
				NetId = netId,
				Revision = revision,
				Hash = 1,
				Position = Vector3.zero
			};

		private static DuplicantDeathStatePacket DeathPacket(
			int netId, ulong lifecycle, ulong revision)
			=> new()
			{
				NetId = netId,
				LifecycleRevision = lifecycle,
				Revision = revision,
				DeathId = "Generic"
			};

		private static bool RejectsDeclaredDeathByteLength(int byteCount)
		{
			try
			{
				using var stream = new MemoryStream();
				using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				{
					writer.Write(1);
					writer.Write((ulong)2);
					writer.Write((ulong)3);
					writer.Write(byteCount);
				}
				stream.Position = 0;
				using var reader = new BinaryReader(stream);
				new DuplicantDeathStatePacket().Deserialize(reader);
				return false;
			}
			catch (InvalidDataException)
			{
				return true;
			}
		}

		private static T RoundTrip<T>(T packet) where T : Networking.Packets.Architecture.IPacket, new()
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				packet.Serialize(writer);
			stream.Position = 0;
			var copy = new T();
			using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
				copy.Deserialize(reader);
			return copy;
		}

		private static bool ThrowsInvalidData(Networking.Packets.Architecture.IPacket packet)
		{
			try
			{
				using var stream = new MemoryStream();
				using var writer = new BinaryWriter(stream);
				packet.Serialize(writer);
				return false;
			}
			catch (InvalidDataException)
			{
				return true;
			}
		}
	}
}
#endif
