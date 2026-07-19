using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using UnityEngine;

namespace ONI_Together.Misc
{
	internal static class StorageSnapshotSync
	{
		private const int MaxItems = 4096;
		internal sealed class SnapshotRequest
		{
			internal Storage Storage;
			internal Dictionary<string, Variant> Data;
			internal string KeyPrefix = "";
			internal byte[] Payload;
			internal string DiseaseReason = "Multiplayer Sync";
			internal ulong SnapshotRevision;
			internal bool ApplyChanges = true;
		}

		internal sealed class SnapshotBatch
		{
			private readonly List<PreparedSnapshot> _snapshots;

			internal SnapshotBatch(List<PreparedSnapshot> snapshots)
			{
				_snapshots = snapshots;
			}

			internal bool Apply()
			{
				return ExecuteBatchPhases(_snapshots, new BatchPhases<PreparedSnapshot>
				{
					Remove = RemoveSnapshotItems,
					Apply = ApplySnapshotItems,
					Verify = MatchesSnapshot,
				});
			}
		}

		internal sealed class Entry
		{
			internal int NetId, PrefabHash;
			internal ulong LifecycleRevision;
			internal float Mass, Temperature;
			internal byte DiseaseIdx;
			internal int DiseaseCount;
		}

		private sealed class PrepareContext
		{
			internal readonly HashSet<Storage> Storages = new();
			internal readonly HashSet<int> DesiredItems = new();
		}

		private sealed class BatchPhases<T>
		{
			internal Action<T> Remove;
			internal Action<T> Apply;
			internal Func<T, bool> Verify;
		}

		private sealed class EntryApplyContext
		{
			internal Storage Storage;
			internal string DiseaseReason;
			internal ulong SnapshotRevision;
		}

		internal sealed class PreparedSnapshot
		{
			internal SnapshotRequest Request;
			internal float Capacity;
			internal List<Entry> Items;
		}

		internal static void Encode(
			Storage storage, Dictionary<string, Variant> values, string keyPrefix = "")
		{
			using var stream = new MemoryStream();
			using var writer = new BinaryWriter(stream);
			writer.Write(storage.capacityKg);
			List<GameObject> items = CollectSnapshotItems(
				storage, prepareIdentityMembership: true);
			writer.Write(items.Count);
			foreach (GameObject item in items)
				WriteEntry(writer, item);
			values[keyPrefix + "stor"] = stream.ToArray();
		}

		internal static void PrepareIdentityMembership(Storage storage)
		{
			if (storage != null)
				CollectSnapshotItems(storage, prepareIdentityMembership: true);
		}

		internal static bool IsSnapshotStateEligible(
			bool itemExists, bool hasPrimaryElement, float mass,
			float temperature, int diseaseCount, int prefabHash)
		{
			return itemExists && hasPrimaryElement && FinitePositive(mass)
			       && FiniteNonNegative(temperature) && diseaseCount >= 0
			       && prefabHash != 0;
		}

		internal static bool ShouldRemoveSnapshotItem(
			bool stateEligible, int netId, bool desired,
			ulong lastItemRevision, ulong snapshotRevision)
		{
			return stateEligible && !desired
			       && (netId == 0 || lastItemRevision <= snapshotRevision);
		}

		internal static List<GameObject> CollectSnapshotItems(
			Storage storage, bool prepareIdentityMembership)
		{
			var items = new List<GameObject>();
			if (storage == null)
				return items;
			foreach (GameObject item in storage.items)
			{
				if (TryGetSnapshotIdentity(
					    item, prepareIdentityMembership, out _))
					items.Add(item);
			}
			return items;
		}

		internal static void Apply(SnapshotRequest request)
		{
			ApplyBatch(new[] { request });
		}

		internal static bool ApplyBatch(IReadOnlyList<SnapshotRequest> requests)
		{
			if (!TryPrepareBatch(requests, out SnapshotBatch batch))
				return false;
			return batch.Apply();
		}

		internal static bool TryPrepareBatch(
			IReadOnlyList<SnapshotRequest> requests, out SnapshotBatch batch)
		{
			batch = null;
			if (!TryPrepareSnapshots(requests, out List<PreparedSnapshot> prepared))
				return false;
			batch = new SnapshotBatch(prepared);
			return true;
		}

#if DEBUG
		internal static bool Matches(Storage storage, byte[] payload)
		{
			if (storage == null || payload == null)
				return false;
			(float capacity, List<Entry> items) = Decode(payload);
			return MatchesSnapshot(new PreparedSnapshot
			{
				Request = new SnapshotRequest { Storage = storage },
				Capacity = capacity,
				Items = items,
			});
		}

		internal static bool VerifyGlobalRemovalBeforeApply()
		{
			var observed = new List<string>();
			int[] targets = { 1, 2, 3 };
			bool matched = ExecuteBatchPhases(targets, new BatchPhases<int>
			{
				Remove = target => observed.Add("remove:" + target),
				Apply = target => observed.Add("apply:" + target),
				Verify = target => { observed.Add("verify:" + target); return true; },
			});
			return matched && string.Join(",", observed) ==
			       "remove:1,remove:2,remove:3,apply:1,apply:2,apply:3," +
			       "verify:1,verify:2,verify:3";
		}
#endif

		private static bool MatchesSnapshot(PreparedSnapshot snapshot)
		{
			Storage storage = snapshot.Request.Storage;
			List<Entry> items = snapshot.Items;
			List<GameObject> actual = CollectSnapshotItems(
				storage, prepareIdentityMembership: false);
			if (storage.capacityKg != snapshot.Capacity || actual.Count != items.Count)
				return false;
			var seen = new HashSet<int>();
			for (int index = 0; index < actual.Count; index++)
			{
				GameObject item = actual[index];
				Entry entry = items[index];
				NetworkIdentity identity = item?.GetNetIdentity();
				PrimaryElement primary = item?.GetComponent<PrimaryElement>();
				Pickupable pickupable = item?.GetComponent<Pickupable>();
				if (identity == null || primary == null
				    || identity.NetId != entry.NetId || !seen.Add(identity.NetId)
				    || item.PrefabID().GetHashCode() != entry.PrefabHash
				    || primary.Mass != entry.Mass
				    || primary.Temperature != entry.Temperature
				    || primary.DiseaseIdx != entry.DiseaseIdx
				    || primary.DiseaseCount != entry.DiseaseCount
				    || pickupable != null && pickupable.storage != storage)
					return false;
			}
			return true;
		}

		private static bool TryPrepareSnapshots(IReadOnlyList<SnapshotRequest> requests,
			out List<PreparedSnapshot> prepared)
		{
			prepared = new List<PreparedSnapshot>(requests?.Count ?? 0);
			if (requests == null)
				return false;
			var context = new PrepareContext();
			foreach (SnapshotRequest request in requests)
			{
				if (!TryPrepare(request, context, out PreparedSnapshot snapshot))
					return false;
				if (snapshot != null)
					prepared.Add(snapshot);
			}
			return true;
		}

		private static bool TryPrepare(
			SnapshotRequest request, PrepareContext context, out PreparedSnapshot prepared)
		{
			prepared = null;
			if (request?.Storage == null)
				return false;
			byte[] payload = request.Payload;
			if (payload == null && request.Data != null
			    && request.Data.TryGetValue(request.KeyPrefix + "stor", out Variant blob))
				payload = blob.ByteArray;
			if (payload == null || !context.Storages.Add(request.Storage))
			{
				DebugConsole.LogError($"[StorageSnapshot] Missing or duplicate target {request.KeyPrefix}stor");
				return false;
			}
			(float capacity, List<Entry> items) = Decode(payload);
			foreach (Entry entry in items)
				if (!context.DesiredItems.Add(entry.NetId)
				    || !CanResolveEntry(request, entry))
					return false;
			prepared = new PreparedSnapshot
			{
				Request = request,
				Capacity = capacity,
				Items = items,
			};
			return true;
		}

		private static bool CanResolveEntry(SnapshotRequest request, Entry entry)
		{
			ulong lifecycle = NetworkIdentityRegistry.GetLastLifecycleRevision(entry.NetId);
			if (lifecycle > entry.LifecycleRevision
			    || lifecycle == entry.LifecycleRevision
			    && NetworkIdentityRegistry.IsLifecycleTombstoned(entry.NetId)
			    || NetworkIdentityRegistry.GetLastStorageItemRevision(entry.NetId)
			    > request.SnapshotRevision)
				return false;
			if (NetworkIdentityRegistry.TryGet(entry.NetId, out NetworkIdentity identity))
			{
				GameObject item = identity?.gameObject;
				return item != null && !item.IsNullOrDestroyed()
				       && item.GetComponent<PrimaryElement>() != null
				       && item.PrefabID().GetHashCode() == entry.PrefabHash;
			}
			Tag tag = new(entry.PrefabHash);
			Element element = ElementLoader.GetElement(tag);
			return element?.substance != null || Assets.GetPrefab(tag) != null;
		}

		private static bool ExecuteBatchPhases<T>(
			IReadOnlyList<T> targets, BatchPhases<T> phases)
		{
			foreach (T target in targets)
				phases.Remove(target);
			foreach (T target in targets)
				phases.Apply(target);
			foreach (T target in targets)
				if (!phases.Verify(target))
					return false;
			return true;
		}

		private static bool TryGetSnapshotIdentity(
			GameObject item, bool prepareIdentityMembership,
			out NetworkIdentity identity)
		{
			identity = null;
			if (!TryGetSnapshotState(item, out _))
				return false;
			identity = item.GetNetIdentity();
			if (prepareIdentityMembership)
			{
				identity ??= item.AddOrGet<NetworkIdentity>();
				if (identity.NetId == 0)
					identity.RegisterIdentity();
				if (identity.NetId != 0)
					EnsureLifecycle(identity.NetId);
			}
			return identity != null && identity.NetId != 0
			       && NetworkIdentityRegistry.GetLastLifecycleRevision(identity.NetId) != 0
			       && !NetworkIdentityRegistry.IsLifecycleTombstoned(identity.NetId);
		}

		private static bool TryGetSnapshotState(
			GameObject item, out PrimaryElement primary)
		{
			primary = null;
			if (item == null || item.IsNullOrDestroyed())
				return false;
			primary = item.GetComponent<PrimaryElement>();
			int prefabHash = item.TryGetComponent<KPrefabID>(out _)
				? item.PrefabID().GetHashCode()
				: 0;
			return IsSnapshotStateEligible(
				true, primary != null,
				primary?.Mass ?? 0f, primary?.Temperature ?? 0f,
				primary?.DiseaseCount ?? -1, prefabHash);
		}

		private static ulong EnsureLifecycle(int netId)
		{
			ulong revision = NetworkIdentityRegistry.GetLastLifecycleRevision(netId);
			return revision != 0 ? revision : NetworkIdentityRegistry.BeginLifecycle(netId);
		}

		private static void WriteEntry(BinaryWriter writer, GameObject item)
		{
			NetworkIdentity identity = item.GetNetIdentity();
			PrimaryElement primary = item.GetComponent<PrimaryElement>();
			writer.Write(identity.NetId);
			writer.Write(EnsureLifecycle(identity.NetId));
			writer.Write(item.PrefabID().GetHashCode());
			writer.Write(primary.Mass);
			writer.Write(primary.Temperature);
			writer.Write(primary.DiseaseIdx);
			writer.Write(primary.DiseaseCount);
		}

		private static (float Capacity, List<Entry> Items) Decode(byte[] payload)
		{
			using var stream = new MemoryStream(payload);
			using var reader = new BinaryReader(stream);
			float capacity = reader.ReadSingle();
			int count = reader.ReadInt32();
			if (!FiniteNonNegative(capacity) || count < 0 || count > MaxItems)
				throw new InvalidDataException("Invalid storage snapshot header");
			var items = new List<Entry>(count);
			var netIds = new HashSet<int>();
			for (int index = 0; index < count; index++)
				items.Add(ReadEntry(reader, netIds));
			if (stream.Position != stream.Length)
				throw new InvalidDataException("Storage snapshot contains trailing bytes");
			return (capacity, items);
		}

		private static Entry ReadEntry(BinaryReader reader, HashSet<int> netIds)
		{
			int netId = reader.ReadInt32();
			ulong lifecycleRevision = reader.ReadUInt64();
			int prefabHash = reader.ReadInt32();
			float mass = reader.ReadSingle();
			float temperature = reader.ReadSingle();
			byte diseaseIdx = reader.ReadByte();
			int diseaseCount = reader.ReadInt32();
			if (netId == 0 || lifecycleRevision == 0 || prefabHash == 0 || !netIds.Add(netId)
			    || !FinitePositive(mass) || !FiniteNonNegative(temperature) || diseaseCount < 0)
				throw new InvalidDataException("Invalid storage snapshot item");
			return new Entry
			{
				NetId = netId,
				LifecycleRevision = lifecycleRevision,
				PrefabHash = prefabHash,
				Mass = mass,
				Temperature = temperature,
				DiseaseIdx = diseaseIdx,
				DiseaseCount = diseaseCount,
			};
		}

		private static void RemoveSnapshotItems(PreparedSnapshot snapshot)
		{
			if (!snapshot.Request.ApplyChanges)
				return;
			snapshot.Request.Storage.capacityKg = snapshot.Capacity;
			RemoveDuplicateListEntries(snapshot.Request.Storage);
			var desiredIds = new HashSet<int>();
			foreach (Entry entry in snapshot.Items)
				desiredIds.Add(entry.NetId);
			RemoveObsoleteItems(snapshot.Request.Storage, desiredIds,
				snapshot.Request.SnapshotRevision);
		}

		private static void RemoveDuplicateListEntries(Storage storage)
		{
			var seen = new HashSet<GameObject>();
			for (int index = storage.items.Count - 1; index >= 0; index--)
			{
				GameObject item = storage.items[index];
				if (item != null && !seen.Add(item))
					storage.items.RemoveAt(index);
			}
		}

		private static void ApplySnapshotItems(PreparedSnapshot snapshot)
		{
			if (!snapshot.Request.ApplyChanges)
				return;
			var context = new EntryApplyContext
			{
				Storage = snapshot.Request.Storage,
				DiseaseReason = snapshot.Request.DiseaseReason,
				SnapshotRevision = snapshot.Request.SnapshotRevision,
			};
			foreach (Entry entry in snapshot.Items)
				ApplyEntry(context, entry);
			ReorderItems(snapshot.Request.Storage, snapshot.Items);
		}

		private static void ReorderItems(Storage storage, IReadOnlyList<Entry> desired)
		{
			var byNetId = new Dictionary<int, GameObject>();
			List<GameObject> canonical = CollectSnapshotItems(
				storage, prepareIdentityMembership: false);
			foreach (GameObject item in canonical)
			{
				int netId = item.GetNetIdentity().NetId;
				if (!byNetId.TryAdd(netId, item))
					return;
			}
			if (canonical.Count != desired.Count)
				return;
			var slots = new List<int>(canonical.Count);
			var canonicalSet = new HashSet<GameObject>(canonical);
			for (int index = 0; index < storage.items.Count; index++)
				if (canonicalSet.Contains(storage.items[index]))
					slots.Add(index);
			for (int targetIndex = 0; targetIndex < slots.Count; targetIndex++)
			{
				if (!byNetId.TryGetValue(desired[targetIndex].NetId, out GameObject item))
					return;
				storage.items[slots[targetIndex]] = item;
			}
		}

		private static void RemoveObsoleteItems(
			Storage storage, HashSet<int> desiredIds, ulong snapshotRevision)
		{
			for (int index = storage.items.Count - 1; index >= 0; index--)
			{
				GameObject item = storage.items[index];
				if (item == null)
					storage.items.RemoveAt(index);
				else if (TryGetSnapshotState(item, out _))
				{
					NetworkIdentity identity = item.GetNetIdentity();
					int netId = identity?.NetId ?? 0;
					if (!ShouldRemoveSnapshotItem(
						    stateEligible: true, netId,
						    desiredIds.Contains(netId),
						    NetworkIdentityRegistry.GetLastStorageItemRevision(netId),
						    snapshotRevision))
						continue;
					storage.Remove(item, do_disease_transfer: false);
					item.GetComponent<Pickupable>()?.RemovedFromStorage();
					if (netId == 0)
						Util.KDestroyGameObject(item);
				}
			}
		}

		private static void ApplyEntry(EntryApplyContext context, Entry entry)
		{
			ulong currentLifecycle = NetworkIdentityRegistry.GetLastLifecycleRevision(entry.NetId);
			if (currentLifecycle > entry.LifecycleRevision
			    || currentLifecycle == entry.LifecycleRevision
			    && NetworkIdentityRegistry.IsLifecycleTombstoned(entry.NetId)
			    || NetworkIdentityRegistry.GetLastStorageItemRevision(entry.NetId)
			    > context.SnapshotRevision)
				return;
			GameObject item = ResolveItem(context.Storage, entry);
			if (item == null || item.PrefabID().GetHashCode() != entry.PrefabHash)
				return;
			if (!NetworkIdentityRegistry.TryBindAuthoritativeLifecycle(
				    item, entry.NetId, entry.LifecycleRevision))
				return;
			item = StoreIfNeeded(context.Storage, item, entry);
			if (item != null)
				ApplyPrimaryElement(item, entry, context.DiseaseReason);
		}

		private static GameObject ResolveItem(Storage storage, Entry entry)
		{
			if (NetworkIdentityRegistry.TryGet(entry.NetId, out NetworkIdentity identity))
				return identity.gameObject;
			Tag tag = new(entry.PrefabHash);
			Element element = ElementLoader.GetElement(tag);
			GameObject item = element != null
				? element.substance.SpawnResource(storage.transform.position, entry.Mass,
					entry.Temperature, entry.DiseaseIdx, entry.DiseaseCount,
					prevent_merge: SpawnPrefabPacket.ShouldPreventElementMerge(entry.NetId))
				: InstantiatePrefab(storage, tag);
			if (item != null && item.AddOrGet<NetworkIdentity>().OverrideNetId(entry.NetId))
				return item;
			if (item != null)
				Util.KDestroyGameObject(item);
			return null;
		}

		private static GameObject InstantiatePrefab(Storage storage, Tag tag)
		{
			GameObject prefab = Assets.GetPrefab(tag);
			if (prefab == null)
				return null;
			GameObject item = GameUtil.KInstantiate(prefab, storage.transform.position, Grid.SceneLayer.Ore);
			item.SetActive(true);
			return item;
		}

		private static void ApplyPrimaryElement(GameObject item, Entry entry, string reason)
		{
			if (!item.TryGetComponent<PrimaryElement>(out PrimaryElement primary))
				return;
			primary.Mass = entry.Mass;
			primary.Temperature = entry.Temperature;
			if (primary.DiseaseCount > 0)
				primary.ModifyDiseaseCount(-primary.DiseaseCount, reason);
			if (entry.DiseaseCount > 0 && entry.DiseaseIdx != byte.MaxValue)
				primary.AddDisease(entry.DiseaseIdx, entry.DiseaseCount, reason);
		}

		private static GameObject StoreIfNeeded(
			Storage storage, GameObject item, Entry entry)
		{
			if (storage.items.Contains(item))
			{
				Pickupable existing = item.GetComponent<Pickupable>();
				if (existing != null && existing.storage != storage)
					existing.OnStore(storage);
				return item;
			}
			Pickupable pickupable = item.GetComponent<Pickupable>();
			GameObject stored = storage.Store(
				item, hide_popups: true, block_events: true,
				do_disease_transfer: false,
				is_deserializing: StorageItemPacket.ShouldUseNonAbsorbingStore(entry.NetId));
			GameObject survivor = stored ?? item;
			if (!ReferenceEquals(survivor, item) || item.IsNullOrDestroyed())
				return null;
			storage.ApplyStoredItemModifiers(item, is_stored: true, is_initializing: false);
			if (pickupable != null && !pickupable.IsNullOrDestroyed())
				pickupable.OnStore(storage);
			return item;
		}

		private static bool FinitePositive(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
		private static bool FiniteNonNegative(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
	}
}
