using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ONI_Together.Misc;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using ONI_Together.Patches.DLC.SpacedOut;
using UnityEngine;

namespace ONI_Together.Networking
{
	[Flags]
	public enum ProductionDesyncDomain : byte
	{
		None = 0,
		Grid = 1,
		EntityLifecycle = 2,
		WorldMembership = 4,
		Storage = 8,
		ClusterRocket = 16,
	}

	public sealed class ProductionStateHashes
	{
		public const int HashLength = 32;
		public byte[] Grid = NewHash();
		public byte[] EntityLifecycle = NewHash();
		public byte[] WorldMembership = NewHash();
		public byte[] Storage = NewHash();
		public byte[] ClusterRocket = NewHash();

		public ProductionDesyncDomain DifferentDomains(ProductionStateHashes other)
		{
			if (other == null)
				return AllDomains;
			ProductionDesyncDomain result = ProductionDesyncDomain.None;
			if (!Grid.SequenceEqual(other.Grid)) result |= ProductionDesyncDomain.Grid;
			if (!EntityLifecycle.SequenceEqual(other.EntityLifecycle)) result |= ProductionDesyncDomain.EntityLifecycle;
			if (!WorldMembership.SequenceEqual(other.WorldMembership)) result |= ProductionDesyncDomain.WorldMembership;
			if (!Storage.SequenceEqual(other.Storage)) result |= ProductionDesyncDomain.Storage;
			if (!ClusterRocket.SequenceEqual(other.ClusterRocket)) result |= ProductionDesyncDomain.ClusterRocket;
			return result;
		}

		internal void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(Grid);
			writer.Write(EntityLifecycle);
			writer.Write(WorldMembership);
			writer.Write(Storage);
			writer.Write(ClusterRocket);
		}

		internal static ProductionStateHashes Deserialize(BinaryReader reader)
		{
			var result = new ProductionStateHashes
			{
				Grid = ReadHash(reader),
				EntityLifecycle = ReadHash(reader),
				WorldMembership = ReadHash(reader),
				Storage = ReadHash(reader),
				ClusterRocket = ReadHash(reader),
			};
			result.Validate();
			return result;
		}

		internal void Validate()
		{
			if (!Valid(Grid) || !Valid(EntityLifecycle) || !Valid(WorldMembership)
			    || !Valid(Storage) || !Valid(ClusterRocket))
				throw new InvalidDataException("Invalid production state hash payload");
		}

		private static byte[] ReadHash(BinaryReader reader)
		{
			byte[] result = reader.ReadBytes(HashLength);
			if (result.Length != HashLength)
				throw new EndOfStreamException("Production state hash payload is truncated");
			return result;
		}

		private static bool Valid(byte[] value) => value?.Length == HashLength;
		private static byte[] NewHash() => new byte[HashLength];
		private const ProductionDesyncDomain AllDomains =
			ProductionDesyncDomain.Grid | ProductionDesyncDomain.EntityLifecycle
			| ProductionDesyncDomain.WorldMembership | ProductionDesyncDomain.Storage
			| ProductionDesyncDomain.ClusterRocket;
	}

	internal static class ProductionStateHash
	{
		internal static ProductionStateHashes CaptureCurrent()
		{
			NetworkIdentity[] identities = NetworkIdentityRegistry.AllIdentities
				.Where(IsHashable).OrderBy(identity => identity.NetId).ToArray();
			return new ProductionStateHashes
			{
				Grid = HashGrid(),
				EntityLifecycle = HashLifecycle(identities),
				WorldMembership = HashWorldMembership(identities),
				Storage = HashStorage(identities),
				ClusterRocket = HashClusterRocket(identities),
			};
		}

		private static byte[] HashGrid()
		{
			return Hash(writer =>
			{
				writer.Write(Grid.CellCount);
				for (int cell = 0; cell < Grid.CellCount; cell++)
					if (Grid.IsValidCell(cell)) WriteCell(writer, cell);
			});
		}

		private static void WriteCell(BinaryWriter writer, int cell)
		{
			float mass = Grid.Mass[cell];
			writer.Write(cell);
			writer.Write(Grid.ElementIdx[cell]);
			writer.Write(NormalizeFloatBits(mass));
			writer.Write(mass == 0f ? 0 : NormalizeFloatBits(Grid.Temperature[cell]));
			writer.Write(Grid.DiseaseIdx[cell]);
			writer.Write(Grid.DiseaseCount[cell]);
		}

		private static byte[] HashLifecycle(NetworkIdentity[] identities)
		{
			return Hash(writer =>
			{
				var live = identities.ToDictionary(identity => identity.NetId);
				var captured = new HashSet<int>();
				var baseline = NetworkIdentityRegistry.GetLifecycleRevisionSnapshot()
					.OrderBy(entry => entry.NetId).ToArray();
				writer.Write(baseline.Length);
				foreach (var entry in baseline)
					WriteLifecycle(writer, entry, live, captured);
				foreach (NetworkIdentity identity in identities)
					if (!captured.Contains(identity.NetId)) WriteUnexpectedLifecycle(writer, identity);
			});
		}

		private static void WriteLifecycle(
			BinaryWriter writer,
			NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry entry,
			Dictionary<int, NetworkIdentity> live,
			HashSet<int> captured)
		{
			live.TryGetValue(entry.NetId, out NetworkIdentity identity);
			writer.Write(entry.NetId);
			writer.Write(identity == null ? 0 : identity.gameObject.PrefabID().GetHashCode());
			writer.Write(identity != null && identity.gameObject.activeSelf);
			writer.Write(entry.Revision);
			writer.Write(entry.Tombstoned);
			captured.Add(entry.NetId);
		}

		private static void WriteUnexpectedLifecycle(BinaryWriter writer, NetworkIdentity identity)
		{
			writer.Write(identity.NetId);
			writer.Write(identity.gameObject.PrefabID().GetHashCode());
			writer.Write(identity.gameObject.activeSelf);
			writer.Write(0UL);
			writer.Write(false);
		}

		private static byte[] HashWorldMembership(NetworkIdentity[] identities)
			=> Hash(writer =>
			{
				writer.Write(identities.Length);
				foreach (NetworkIdentity identity in identities) WriteWorldMembership(writer, identity);
			});

		private static void WriteWorldMembership(BinaryWriter writer, NetworkIdentity identity)
		{
			Vector3 position = identity.transform.position;
			EntityPositionHandler handler = identity.GetComponent<EntityPositionHandler>();
			bool authoritativeClient = MultiplayerSession.IsClient && handler?.serverTimestamp > 0;
			if (MultiplayerSession.IsClient && handler?.serverTimestamp > 0)
				position = handler.serverPosition;
			writer.Write(identity.NetId);
			writer.Write(identity.gameObject.GetMyWorldId());
			writer.Write(Grid.PosToCell(position));
			writer.Write(NormalizeFloatBits(position.x));
			writer.Write(NormalizeFloatBits(position.y));
			writer.Write(NormalizeFloatBits(position.z));
			WriteNavigation(writer, handler, authoritativeClient);
		}

		private static void WriteNavigation(
			BinaryWriter writer, EntityPositionHandler handler, bool authoritativeClient)
		{
			writer.Write(handler != null);
			writer.Write(authoritativeClient ? handler.serverFlipX : handler?.kbac?.FlipX ?? false);
			writer.Write(authoritativeClient ? handler.serverFlipY : handler?.kbac?.FlipY ?? false);
			NavType nav = authoritativeClient ? handler.serverNavType : CurrentNavType(handler);
			writer.Write((byte)nav);
		}

		private static NavType CurrentNavType(EntityPositionHandler handler)
			=> handler?.navigator != null && handler.navigator.CurrentNavType != NavType.NumNavTypes
				? handler.navigator.CurrentNavType : NavType.Floor;

		private static byte[] HashStorage(NetworkIdentity[] identities)
		{
			var records = identities
				.Select(identity => (Identity: identity,
					Storages: identity.gameObject.GetComponents<Storage>()))
				.Where(record => ShouldHashStorageOwner(record.Storages.Length))
				.ToArray();
			return Hash(writer =>
			{
				writer.Write(records.Length);
				foreach (var record in records)
					WriteStorages(writer, record.Identity, record.Storages);
			});
		}

		internal static bool ShouldHashStorageOwner(int storageCount) => storageCount > 0;

		private static void WriteStorages(
			BinaryWriter writer, NetworkIdentity identity, Storage[] storages)
		{
			writer.Write(identity.NetId);
			writer.Write(storages.Length);
			for (int index = 0; index < storages.Length; index++)
			{
				Storage storage = storages[index];
				writer.Write(index);
				writer.Write(NormalizeFloatBits(storage.capacityKg));
				List<GameObject> items = StorageSnapshotSync.CollectSnapshotItems(storage, false);
				writer.Write(items.Count);
				for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
					WriteStoredItem(writer, items[itemIndex], itemIndex);
			}
		}

		private static void WriteStoredItem(BinaryWriter writer, GameObject item, int index)
		{
			PrimaryElement primary = item.GetComponent<PrimaryElement>();
			GetLinkedStorageAddress(item, out int linkedNetId, out int linkedIndex);
			writer.Write(index);
			writer.Write(item.GetComponent<NetworkIdentity>()?.NetId ?? 0);
			writer.Write(linkedNetId);
			writer.Write(linkedIndex);
			writer.Write(item.PrefabID().GetHashCode());
			writer.Write(NormalizeFloatBits(primary?.Mass ?? 0f));
			writer.Write(NormalizeFloatBits(primary?.Temperature ?? 0f));
			writer.Write(primary?.DiseaseIdx ?? byte.MaxValue);
			writer.Write(primary?.DiseaseCount ?? 0);
		}

		private static void GetLinkedStorageAddress(
			GameObject item, out int storageNetId, out int storageIndex)
		{
			Storage linked = item.GetComponent<Pickupable>()?.storage;
			NetworkIdentity owner = linked?.GetComponent<NetworkIdentity>();
			storageNetId = owner?.NetId ?? 0;
			storageIndex = -1;
			if (linked == null || owner == null) return;
			storageIndex = Array.IndexOf(owner.gameObject.GetComponents<Storage>(), linked);
		}

		private static byte[] HashClusterRocket(NetworkIdentity[] identities)
			=> Hash(writer =>
			{
				foreach (NetworkIdentity identity in identities) WriteClusterRocket(writer, identity);
			});

		private static void WriteClusterRocket(BinaryWriter writer, NetworkIdentity identity)
		{
			ClusterGridEntity cluster = identity.GetComponent<ClusterGridEntity>();
			RocketModuleCluster module = identity.GetComponent<RocketModuleCluster>();
			RocketControlStation station = identity.GetComponent<RocketControlStation>();
			RocketSettingsPacketData settings = null;
			bool hasSettings = module != null && RocketSettingsSync.TryCapture(
				module.CraftInterface?.GetClusterDestinationSelector(), out settings);
			if (!hasSettings && station != null)
				hasSettings = RocketSettingsSync.TryCapture(station, out settings);
			if (cluster == null && !hasSettings) return;
			writer.Write(identity.NetId);
			writer.Write(cluster != null);
			writer.Write(cluster?.Location.q ?? 0);
			writer.Write(cluster?.Location.r ?? 0);
			writer.Write(hasSettings);
			if (hasSettings) WriteRocketSettings(writer, settings);
		}

		private static void WriteRocketSettings(BinaryWriter writer, RocketSettingsPacketData data)
		{
			writer.Write((byte)data.TargetKind);
			writer.Write(data.TargetLifecycleRevision);
			writer.Write(data.HasDestination);
			writer.Write(data.DestinationQ);
			writer.Write(data.DestinationR);
			writer.Write(data.HasPad);
			writer.Write(data.PadNetId);
			writer.Write(data.Repeat);
			writer.Write(data.RestrictWhenGrounded);
			writer.Write(data.HasCraftState);
			writer.Write(data.CraftLocationQ);
			writer.Write(data.CraftLocationR);
			writer.Write((byte)data.CraftPhase);
			writer.Write(data.HasCurrentPad);
			writer.Write(data.CurrentPadNetId);
		}

		private static byte[] Hash(Action<BinaryWriter> write)
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				write(writer);
			stream.Position = 0;
			using var sha = SHA256.Create();
			return sha.ComputeHash(stream);
		}

		private static bool IsHashable(NetworkIdentity identity)
			=> !identity.IsNullOrDestroyed() && !identity.gameObject.IsNullOrDestroyed()
			   && identity.NetId != 0;

		private static int NormalizeFloatBits(float value)
		{
			if (value == 0f) return 0;
			if (float.IsNaN(value)) return unchecked((int)0x7fc00000);
			return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
		}
	}
}
