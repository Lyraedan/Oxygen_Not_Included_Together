using System;
using System.IO;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking;
using ONI_Together.Networking.Components.StructureStateSyncers;
using ONI_Together.Misc;
using System.Collections.Generic;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests;

public static class StorageTransferTests
{
	[UnitTest(name: "Storage snapshot: periodic delivery is reliable", category: "Networking")]
	public static UnitTestResult PeriodicSnapshotUsesReliableDelivery()
	{
		return (StorageStateSyncer.SnapshotSendMode & PacketSendMode.Reliable) != 0
			? UnitTestResult.Pass("Large storage snapshots cannot be dropped by the unreliable MTU gate")
			: UnitTestResult.Fail("Periodic storage snapshots still use unreliable datagrams");
	}

	[UnitTest(name: "Storage transfer: item identity roundtrips", category: "Networking")]
	public static UnitTestResult ItemIdentityRoundtrips()
	{
		var packet = new StorageItemPacket
		{
			NetId = 1001,
			StorageNetId = 2002,
			FxPrefix = Storage.FXPrefix.Delivered,
			DoDiseaseTransfer = true,
			ConsumedPrefabHash = 3003,
			ConsumedAmount = 4.5f,
			Revision = 44
		};

		StorageItemPacket copy = Roundtrip(packet);
		if (copy.NetId != 1001 || copy.StorageNetId != 2002
		    || copy.FxPrefix != Storage.FXPrefix.Delivered || !copy.DoDiseaseTransfer
		    || copy.ConsumedPrefabHash != 3003 || copy.ConsumedAmount != 4.5f
		    || copy.Revision != 44)
			return UnitTestResult.Fail("Storage transfer metadata changed during roundtrip");
		if (copy is not IHostOnlyPacket)
			return UnitTestResult.Fail("Storage transfer is not host-authoritative");

		return UnitTestResult.Pass("Storage transfer preserves item and destination identities");
	}

	[UnitTest(name: "Storage transfer: malformed metadata rejected", category: "Networking")]
	public static UnitTestResult MalformedMetadataRejected()
	{
		if (!DeserializeThrows(1, 0, Storage.FXPrefix.Delivered, 1f)
		    || !DeserializeThrows(1, 1, (Storage.FXPrefix)99, 1f)
		    || !DeserializeThrows(1, 1, Storage.FXPrefix.Delivered, float.NaN)
		    || !DeserializeThrows(1, 1, Storage.FXPrefix.Delivered, -1f))
			return UnitTestResult.Fail("Malformed storage metadata was accepted");

		return UnitTestResult.Pass("Storage identity, operation, and amount bounds are enforced");
	}

	[UnitTest(name: "Storage transfer: snapshot and item revisions reject stale intent", category: "Networking")]
	public static UnitTestResult SnapshotAndItemRevisionsRejectStaleIntent()
	{
		if (!StorageItemPacket.ShouldApplyRevision(10, 11, 9, 8, 12))
			return UnitTestResult.Fail("New storage intent was rejected");
		if (StorageItemPacket.ShouldApplyRevision(12, 0, 0, 0, 12))
			return UnitTestResult.Fail("Snapshot-equal storage intent was accepted");
		if (StorageItemPacket.ShouldApplyRevision(0, 12, 0, 0, 11))
			return UnitTestResult.Fail("Older per-item storage intent was accepted");
		if (StorageItemPacket.ShouldApplyRevision(0, 0, 12, 0, 11))
			return UnitTestResult.Fail("Storage intent older than item despawn was accepted");
		if (StorageItemPacket.ShouldApplyRevision(0, 0, 0, 12, 11))
			return UnitTestResult.Fail("Storage intent older than storage lifecycle was accepted");

		return UnitTestResult.Pass("Snapshot, item, and both lifecycle revisions enforce latest intent");
	}

	[UnitTest(name: "Storage transfer: authoritative item disables client absorption", category: "Networking")]
	public static UnitTestResult AuthoritativeItemDisablesClientAbsorption()
	{
		if (!StorageItemPacket.ShouldUseNonAbsorbingStore(authoritativeNetId: 42)
		    || StorageItemPacket.ShouldUseNonAbsorbingStore(authoritativeNetId: 0))
			return UnitTestResult.Fail(
				"Client storage could choose a second stack survivor for an authoritative item");
		return UnitTestResult.Pass(
			"The host-selected survivor is stored without any client-side absorption");
	}

	[UnitTest(name: "Storage transfer: delivered disease replay includes merge survivors", category: "Networking")]
	public static UnitTestResult DeliveredDiseaseReplayIncludesMergeSurvivors()
	{
		if (!StorageItemPacket.ShouldReplayDiseaseTransfer(
			    Storage.FXPrefix.Delivered, enabled: true)
		    || StorageItemPacket.ShouldReplayDiseaseTransfer(
			    Storage.FXPrefix.Delivered, enabled: false)
		    || StorageItemPacket.ShouldReplayDiseaseTransfer(
			    Storage.FXPrefix.PickedUp, enabled: true))
			return UnitTestResult.Fail(
				"Disease replay depends on whether Storage.Store kept the source object");
		return UnitTestResult.Pass(
			"Every delivered item replays storage disease before exact item state");
	}

	[UnitTest(name: "Storage snapshot: preserves live item identity", category: "Networking")]
	public static UnitTestResult SnapshotPreservesLiveItemIdentity()
	{
		Storage storage = SelectTool.Instance?.selected?.GetComponent<Storage>();
		if (storage == null || storage.items.Count == 0)
			return UnitTestResult.Skip("Select a non-empty storage building");

		var before = new List<(GameObject Item, int NetId)>();
		foreach (GameObject item in storage.items)
		{
			if (item != null)
				before.Add((item, item.GetNetIdentity()?.NetId ?? 0));
		}
		var state = new Dictionary<string, Variant>();
		StorageSnapshotSync.Encode(storage, state);
		StorageSnapshotSync.Apply(new StorageSnapshotSync.SnapshotRequest
		{
			Storage = storage,
			Data = state,
			SnapshotRevision = ulong.MaxValue,
		});

		foreach (var entry in before)
		{
			if (!storage.items.Contains(entry.Item) || entry.Item.GetNetIdentity()?.NetId != entry.NetId)
				return UnitTestResult.Fail("Storage snapshot replaced an existing item identity");
		}
		return UnitTestResult.Pass("Storage snapshot keeps existing GameObjects and NetIds");
	}

#if DEBUG
	[UnitTest(name: "Storage snapshot: removes globally before applying", category: "Networking")]
	public static UnitTestResult SnapshotUsesGlobalTwoPhaseCommit()
	{
		return StorageSnapshotSync.VerifyGlobalRemovalBeforeApply()
			? UnitTestResult.Pass("All storage removals precede every add and verification")
			: UnitTestResult.Fail("Storage batch interleaved removal with add/relink");
	}
#endif

	private static StorageItemPacket Roundtrip(StorageItemPacket packet)
	{
		using var stream = new MemoryStream();
		using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
			packet.Serialize(writer);
		stream.Position = 0;
		var copy = new StorageItemPacket();
		using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
			copy.Deserialize(reader);
		return copy;
	}

	private static bool DeserializeThrows(int netId, int storageNetId, Storage.FXPrefix operation, float amount)
	{
		try
		{
			Roundtrip(new StorageItemPacket
			{
				NetId = netId,
				StorageNetId = storageNetId,
				FxPrefix = operation,
				ConsumedAmount = amount
			});
			return false;
		}
		catch (InvalidDataException)
		{
			return true;
		}
	}
}
