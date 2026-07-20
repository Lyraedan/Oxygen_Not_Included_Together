using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.World;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests;

public static class LifecyclePacketTests
{
    [UnitTest(name: "Lifecycle packets: registry activation", category: "Networking")]
    public static UnitTestResult RegistryActivation()
    {
        if (!CanCreateRegistered<SpawnPrefabPacket>())
            return UnitTestResult.Fail("PacketRegistry could not create SpawnPrefabPacket");
        if (!CanCreateRegistered<DespawnEntityPacket>())
            return UnitTestResult.Fail("PacketRegistry could not create DespawnEntityPacket");
		if (!CanCreateRegistered<DuplicantDeathStatePacket>())
			return UnitTestResult.Fail("PacketRegistry could not create DuplicantDeathStatePacket");

		return UnitTestResult.Pass("Lifecycle and death packets are registered and Activator-compatible");
    }

    [UnitTest(name: "Lifecycle packets: serialization roundtrip", category: "Networking")]
    public static UnitTestResult SerializationRoundtrip()
    {
        var spawn = new SpawnPrefabPacket(123, 456, new Vector3(1f, 2f, 3f), 4f, 5f, 6, 7)
        {
            IsActive = false,
            Revision = 8,
            WorldId = 9,
            BindExistingOnly = true
        };
        var spawnCopy = Roundtrip(spawn, new SpawnPrefabPacket());
        if (spawnCopy.NetId != spawn.NetId || spawnCopy.Hash != spawn.Hash ||
            spawnCopy.Position != spawn.Position || spawnCopy.IsActive != spawn.IsActive ||
            !spawnCopy.HasElementData || spawnCopy.Mass != spawn.Mass ||
            spawnCopy.Temperature != spawn.Temperature ||
            spawnCopy.DiseaseIndex != spawn.DiseaseIndex || spawnCopy.DiseaseCount != spawn.DiseaseCount ||
            spawnCopy.Revision != spawn.Revision || spawnCopy.WorldId != spawn.WorldId ||
            spawnCopy.BindExistingOnly != spawn.BindExistingOnly)
            return UnitTestResult.Fail("SpawnPrefabPacket did not roundtrip");

        var despawn = new DespawnEntityPacket(789) { Revision = 10 };
        var despawnCopy = Roundtrip(despawn, new DespawnEntityPacket());
        if (despawnCopy.NetId != despawn.NetId || despawnCopy.Revision != despawn.Revision)
            return UnitTestResult.Fail("DespawnEntityPacket did not roundtrip");

        return UnitTestResult.Pass("Lifecycle packet payloads roundtrip");
    }

    [UnitTest(name: "Lifecycle packets: authority and idempotence", category: "Networking")]
    public static UnitTestResult AuthorityAndIdempotence()
    {
        if (SpawnPrefabPacket.ShouldApply(true, true, false))
            return UnitTestResult.Fail("Host accepted SpawnPrefabPacket");
        if (SpawnPrefabPacket.ShouldApply(false, false, false))
            return UnitTestResult.Fail("Client accepted non-host SpawnPrefabPacket");
		if (!SpawnPrefabPacket.ShouldApply(false, true, true))
			return UnitTestResult.Fail("Client rejected an authoritative occupied-NetId reconciliation");
		if (SpawnPrefabPacket.ShouldApply(false, true, true, 8, 8, tombstoned: false))
			return UnitTestResult.Fail("Client accepted duplicate SpawnPrefabPacket revision");
		if (!SpawnPrefabPacket.ShouldApply(false, true, false, 8, 8, tombstoned: false))
			return UnitTestResult.Fail("Client rejected same-revision stale-owner reconstruction");
		if (SpawnPrefabPacket.ShouldApply(false, true, false, 8, 8, tombstoned: true))
			return UnitTestResult.Fail("Client resurrected a same-revision tombstone");
        if (!SpawnPrefabPacket.ShouldApply(false, true, false))
            return UnitTestResult.Fail("Client rejected authoritative new SpawnPrefabPacket");
        if (DespawnEntityPacket.ShouldApply(true, false) || DespawnEntityPacket.ShouldApply(false, false))
            return UnitTestResult.Fail("DespawnEntityPacket authority gate failed");
        if (!DespawnEntityPacket.ShouldApply(false, true))
            return UnitTestResult.Fail("Client rejected authoritative DespawnEntityPacket");

        return UnitTestResult.Pass("Lifecycle authority and spawn idempotence gates hold");
    }

    [UnitTest(name: "Lifecycle packets: tombstone rejects stale spawn", category: "Networking")]
    public static UnitTestResult TombstoneRejectsStaleSpawn()
    {
        if (!DespawnEntityPacket.ShouldApply(false, true, 4, 5))
            return UnitTestResult.Fail("Authoritative newer despawn was rejected");
        if (SpawnPrefabPacket.ShouldApply(false, true, false, 5, 4, tombstoned: false))
            return UnitTestResult.Fail("Spawn older than tombstone was accepted");
        if (SpawnPrefabPacket.ShouldApply(false, true, false, 5, 5, tombstoned: true))
            return UnitTestResult.Fail("Spawn equal to tombstone was accepted");
        if (!SpawnPrefabPacket.ShouldApply(false, true, false, 5, 6, tombstoned: true))
            return UnitTestResult.Fail("New lifecycle after tombstone was rejected");

        return UnitTestResult.Pass("Despawn-before-spawn converges by lifecycle revision");
    }

	[UnitTest(name: "Lifecycle packets: element state bounds", category: "Networking")]
	public static UnitTestResult ElementStateBounds()
	{
		if (!SpawnPrefabPacket.IsValidElementState(1f, 300f, 0)
		    || SpawnPrefabPacket.IsValidElementState(0f, 300f, 0)
		    || SpawnPrefabPacket.IsValidElementState(float.NaN, 300f, 0)
		    || SpawnPrefabPacket.IsValidElementState(1f, float.PositiveInfinity, 0)
		    || SpawnPrefabPacket.IsValidElementState(-1f, 300f, 0)
		    || SpawnPrefabPacket.IsValidElementState(1f, 300f, -1))
			return UnitTestResult.Fail("Spawn element bounds accepted malformed absolute state");

		return UnitTestResult.Pass(
			"Claimed spawn state rejects non-material element lifecycles");
	}

	[UnitTest(
		name: "Lifecycle baseline rebuilds persistent element resources",
		category: "Networking")]
	public static UnitTestResult PersistentElementResourcesAreRebuildable()
	{
		if (SpawnPrefabPacket.RequiresExistingSnapshotBinding(
			    false, true, true, true))
			return UnitTestResult.Fail(
				"A fully-described element resource was restricted to an existing instance");
		if (!SpawnPrefabPacket.RequiresExistingSnapshotBinding(
			    false, true, true, false))
			return UnitTestResult.Fail(
				"A persistent non-element object lost its existing-instance requirement");
		if (!SpawnPrefabPacket.RequiresExistingSnapshotBinding(
			    true, false, false, false))
			return UnitTestResult.Fail(
				"An explicitly bound non-element object became freely creatable");
		return UnitTestResult.Pass(
			"Element resources can be rebuilt while persistent prefabs remain strict");
	}

	[UnitTest(
		name: "Constructable lifecycle waits for build-state materialization",
		category: "Networking")]
	public static UnitTestResult ConstructableWaitsForBuildState()
	{
		if (!SpawnPrefabPacket.RequiresBuildStateMaterialization(hasConstructable: true)
		    || SpawnPrefabPacket.RequiresBuildStateMaterialization(hasConstructable: false)
		    || !BuildStatePolicyIsUsed())
			return UnitTestResult.Fail(
				"Generic lifecycle can activate a Constructable without material tags");
		return UnitTestResult.Pass(
			"Every Constructable binds only after BuildState initializes its materials");
	}

	private static bool BuildStatePolicyIsUsed()
	{
		const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
		MethodInfo binding = typeof(SpawnPrefabPacket).GetMethod(
			"RequiresExistingSnapshotBinding", flags, null,
			new[] { typeof(NetworkIdentity), typeof(GameObject), typeof(bool) }, null);
		MethodInfo policy = typeof(SpawnPrefabPacket).GetMethod(
			nameof(SpawnPrefabPacket.RequiresBuildStateMaterialization), flags);
		byte[] il = binding?.GetMethodBody()?.GetILAsByteArray();
		if (il == null || policy == null)
			return false;
		byte[] token = BitConverter.GetBytes(policy.MetadataToken);
		return Enumerable.Range(0, il.Length - token.Length + 1).Any(index =>
			il.Skip(index).Take(token.Length).SequenceEqual(token));
	}

	[UnitTest(
		name: "Lifecycle baseline repositions occupied same-world identity",
		category: "Networking")]
	public static UnitTestResult OccupiedSameWorldIdentityCanReconcile()
	{
		if (!SpawnPrefabPacket.CanReconcileOccupiedIdentity(
			    samePrefab: true, occupiedWorldId: 2, snapshotWorldId: 2))
			return UnitTestResult.Fail("Position drift blocked an occupied authority identity");
		if (SpawnPrefabPacket.CanReconcileOccupiedIdentity(
			    samePrefab: false, occupiedWorldId: 2, snapshotWorldId: 2))
			return UnitTestResult.Fail("Different prefab was accepted for an occupied NetId");
		if (SpawnPrefabPacket.CanReconcileOccupiedIdentity(
			    samePrefab: true, occupiedWorldId: 2, snapshotWorldId: 3))
			return UnitTestResult.Fail("Cross-world occupied identity was accepted implicitly");
		if (SpawnPrefabPacket.CanReconcileOccupiedIdentity(
			    unavailable: true, samePrefab: true,
			    occupiedWorldId: 2, snapshotWorldId: 2))
			return UnitTestResult.Fail("Pending destruction was accepted as an occupied identity");
		return UnitTestResult.Pass(
			"Occupied same-prefab identity can receive authoritative position correction");
	}

	[UnitTest(name: "Lifecycle packets: baseline replacement is atomic", category: "Networking")]
	public static UnitTestResult BaselineReplacementIsAtomic()
	{
		var original = NetworkIdentityRegistry.GetLifecycleRevisionSnapshot().ToArray();
		var baseline = new[]
		{
			new NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry(-101, 20, false),
			new NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry(-102, 21, true),
		};
		try
		{
			if (!NetworkIdentityRegistry.TryReplaceLifecycleRevisionBaseline(baseline))
				return UnitTestResult.Fail("Valid lifecycle baseline was rejected");
			if (NetworkIdentityRegistry.GetLastLifecycleRevision(-101) != 20
			    || NetworkIdentityRegistry.IsLifecycleTombstoned(-101)
			    || NetworkIdentityRegistry.GetLastLifecycleRevision(-102) != 21
			    || !NetworkIdentityRegistry.IsLifecycleTombstoned(-102))
				return UnitTestResult.Fail("Lifecycle baseline did not replace journal state");

			var invalid = new[]
			{
				new NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry(-103, 22, false),
				new NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry(-103, 23, true),
			};
			if (NetworkIdentityRegistry.TryReplaceLifecycleRevisionBaseline(invalid))
				return UnitTestResult.Fail("Duplicate lifecycle baseline entry was accepted");
			if (NetworkIdentityRegistry.GetLastLifecycleRevision(-101) != 20)
				return UnitTestResult.Fail("Invalid baseline partially replaced journal state");

			return UnitTestResult.Pass("Lifecycle baseline replacement is validated and atomic");
		}
		finally
		{
			NetworkIdentityRegistry.TryReplaceLifecycleRevisionBaseline(original);
		}
	}

    private static bool CanCreateRegistered<T>() where T : IPacket, new()
    {
        if (!PacketRegistry.HasRegisteredPacket(typeof(T)))
            return false;

        int packetId = PacketRegistry.GetPacketId(new T());
        return PacketRegistry.Create(packetId) is T;
    }

    private static T Roundtrip<T>(T source, T target) where T : IPacket
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            source.Serialize(writer);
        stream.Position = 0;
        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
            target.Deserialize(reader);
        return target;
    }
}
