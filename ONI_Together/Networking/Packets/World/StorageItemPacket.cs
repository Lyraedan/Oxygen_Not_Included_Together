using System;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;
using UnityEngine;
using static Storage;
using Klei;
using Shared.Interfaces.Networking;
using System.Linq;

namespace ONI_Together.Networking.Packets.World
{
    /// <summary>
    /// Modified version of GroundItemPickedUpPacket
    /// </summary>
    public class StorageItemPacket : IPacket, IBulkablePacket, IHostOnlyPacket
    {
        private static readonly Dictionary<int, StorageItemPacket> PendingTransfers = [];

        public int NetId;
        public int StorageNetId;
		public ulong Revision;
        public FXPrefix FxPrefix;
        public bool DoDiseaseTransfer;

        public int ConsumedPrefabHash;
        public float ConsumedAmount; // Mass / Units
		public bool HasElementState;
		public float ElementMass;
		public float ElementTemperature;
		public byte ElementDiseaseIdx;
		public int ElementDiseaseCount;

        public int MaxPackSize => 500;

        public uint IntervalMs => 250;

        public static bool TryApplyPending(int netId, GameObject item)
        {
            using var _ = Profiler.Scope();
            if (!PendingTransfers.TryGetValue(netId, out StorageItemPacket packet))
                return false;
			if (!NetworkIdentityRegistry.IsCurrentStorageTransferRevision(
					packet.StorageNetId, packet.NetId, packet.Revision))
			{
				PendingTransfers.Remove(netId);
				return false;
			}
            if (!NetworkIdentityRegistry.TryGetComponent<Storage>(packet.StorageNetId, out var storage))
                return false;

            PendingTransfers.Remove(netId);
            packet.ApplyTransfer(item, storage);
            return true;
        }

		public static void TryApplyPendingForStorage(int storageNetId, Storage storage)
		{
			foreach (StorageItemPacket packet in PendingTransfers.Values
				         .Where(packet => packet.StorageNetId == storageNetId).ToArray())
			{
				if (!NetworkIdentityRegistry.IsCurrentStorageTransferRevision(
						packet.StorageNetId, packet.NetId, packet.Revision))
				{
					PendingTransfers.Remove(packet.NetId);
					continue;
				}
				if (!NetworkIdentityRegistry.TryGet(packet.NetId, out var identity))
					continue;
				PendingTransfers.Remove(packet.NetId);
				packet.ApplyTransfer(identity.gameObject, storage);
			}
		}

        public static void ClearPending()
        {
            using var _ = Profiler.Scope();
            int n = PendingTransfers.Count;
            PendingTransfers.Clear();
            DebugConsole.Log($"[PendingStorageTransfer] cleared count={n}");
        }

		internal static void CancelPending(int netId) => PendingTransfers.Remove(netId);

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();
			if (Revision == 0)
				Revision = NetworkIdentityRegistry.NextAuthorityRevision();
            writer.Write(NetId);
            writer.Write(StorageNetId);
			writer.Write(Revision);
            writer.Write((int)FxPrefix);
            writer.Write(DoDiseaseTransfer);

            writer.Write(ConsumedPrefabHash);
            writer.Write(ConsumedAmount);
			writer.Write(HasElementState);
			if (HasElementState)
			{
				writer.Write(ElementMass);
				writer.Write(ElementTemperature);
				writer.Write(ElementDiseaseIdx);
				writer.Write(ElementDiseaseCount);
			}
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();
            NetId = reader.ReadInt32();
            StorageNetId = reader.ReadInt32();
            Revision = reader.ReadUInt64();
            FxPrefix = (FXPrefix)reader.ReadInt32();
            DoDiseaseTransfer = reader.ReadBoolean();

            ConsumedPrefabHash = reader.ReadInt32();
            ConsumedAmount = reader.ReadSingle();
			HasElementState = reader.ReadBoolean();
			if (HasElementState)
			{
				ElementMass = reader.ReadSingle();
				ElementTemperature = reader.ReadSingle();
				ElementDiseaseIdx = reader.ReadByte();
				ElementDiseaseCount = reader.ReadInt32();
			}
			if (StorageNetId == 0 || Revision == 0
                || (FxPrefix != FXPrefix.Delivered && FxPrefix != FXPrefix.PickedUp)
				|| !FiniteNonNegative(ConsumedAmount)
				|| HasElementState && (!FiniteNonNegative(ElementMass)
					|| !FiniteNonNegative(ElementTemperature) || ElementDiseaseCount < 0))
                throw new InvalidDataException("Invalid storage transfer metadata");
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            // FX Only
            if (NetId == 0)
            {
                if (!NetworkIdentityRegistry.TryGetComponent<Storage>(StorageNetId, out var container))
                    return;

                if (PopFXManager.Instance != null && ConsumedPrefabHash != 0)
                {
                    DisplayFX(null, container); // FX Only
                }
                return;
            }

			if (!NetworkIdentityRegistry.TryAcceptStorageTransferRevision(StorageNetId, NetId, Revision))
				return;

            if (!NetworkIdentityRegistry.TryGetComponent<Pickupable>(NetId, out var pickupable))
            {
                if (FxPrefix == FXPrefix.Delivered)
                    PendingTransfers[NetId] = this;
				else
					PendingTransfers.Remove(NetId);
                DebugConsole.LogWarning($"[StoreItemPacket] Pickupable NetId {NetId} not yet registered");
                return;
            }

            if (!NetworkIdentityRegistry.TryGetComponent<Storage>(StorageNetId, out var storage))
            {
                if (FxPrefix == FXPrefix.Delivered)
                    PendingTransfers[NetId] = this;
				else
					PendingTransfers.Remove(NetId);
                DebugConsole.LogWarning($"[StoreItemPacket] No storage found with NetID: {StorageNetId}");
                return;
            }

            ApplyTransfer(pickupable.gameObject, storage);
        }

		private void ApplyTransfer(GameObject item, Storage storage)
		{
            if (item == null || storage == null)
                return;
			if (!NetworkIdentityRegistry.IsCurrentStorageTransferRevision(StorageNetId, NetId, Revision))
				return;

			Pickupable pickupable = item.GetComponent<Pickupable>();
			if (FxPrefix == FXPrefix.Delivered)
			{
				if (!TryStoreDeliveredItem(
					    item, storage, pickupable, out GameObject authoritativeItem))
					return;
				if (ShouldReplayDiseaseTransfer(FxPrefix, DoDiseaseTransfer))
					HandleDiseaseTransfer(authoritativeItem, storage);
				ApplyAuthoritativeElementState(authoritativeItem);
			}
			else if (storage.items.Contains(item))
			{
				storage.Remove(item, do_disease_transfer: false);
				pickupable?.RemovedFromStorage();
			}

			DisplayFX(item, storage);
		}

		private bool TryStoreDeliveredItem(
			GameObject item, Storage storage, Pickupable pickupable,
			out GameObject authoritativeItem)
		{
			authoritativeItem = item;
			if (storage.items.Contains(item))
				return true;
			GameObject stored = storage.Store(
				item, hide_popups: true, block_events: true,
				do_disease_transfer: false,
				is_deserializing: ShouldUseNonAbsorbingStore(NetId));
			authoritativeItem = stored ?? item;
			if (!ReferenceEquals(authoritativeItem, item)
			    || item.IsNullOrDestroyed())
			{
				RequestAuthoritativeStorageState();
				return false;
			}
			storage.ApplyStoredItemModifiers(
				item, is_stored: true, is_initializing: false);
			if (pickupable != null && !pickupable.IsNullOrDestroyed())
				pickupable.OnStore(storage);
			return true;
		}

		private void RequestAuthoritativeStorageState()
		{
			if (!MultiplayerSession.IsClient
			    || !GameClient.CanSendRuntimeRequests(GameClient.State))
				return;
			PacketSender.SendToHost(new StructureStateRequestPacket
			{
				NetId = StorageNetId,
				RequesterId = MultiplayerSession.LocalUserID,
			}, PacketSendMode.ReliableImmediate);
		}

		private void ApplyAuthoritativeElementState(GameObject item)
		{
			if (!HasElementState || item == null
			    || !item.TryGetComponent<PrimaryElement>(out var primary))
				return;
			primary.Mass = ElementMass;
			primary.Temperature = ElementTemperature;
			if (primary.DiseaseCount > 0)
				primary.ModifyDiseaseCount(-primary.DiseaseCount, "ONI Together storage sync");
			if (ElementDiseaseCount > 0 && ElementDiseaseIdx != byte.MaxValue)
				primary.AddDisease(ElementDiseaseIdx, ElementDiseaseCount,
					"ONI Together storage sync");
		}

		private static bool FiniteNonNegative(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;

		internal static bool ShouldApplyRevision(
			ulong lastSnapshotRevision,
			ulong lastItemRevision,
			ulong lastItemLifecycleRevision,
			ulong lastStorageLifecycleRevision,
			ulong incomingRevision)
		{
			return incomingRevision != 0
			       && incomingRevision > lastSnapshotRevision
			       && incomingRevision > lastItemRevision
			       && incomingRevision > lastItemLifecycleRevision
			       && incomingRevision > lastStorageLifecycleRevision;
		}

		internal static bool ShouldUseNonAbsorbingStore(int authoritativeNetId)
			=> authoritativeNetId != 0;

		internal static bool ShouldReplayDiseaseTransfer(FXPrefix operation, bool enabled)
			=> operation == FXPrefix.Delivered && enabled;

        public void DisplayFX(GameObject pickupable, Storage storage)
        {
			if (PopFXManager.Instance == null)
				return;
            Tag prefabTag = new Tag(ConsumedPrefabHash);
            GameObject prefab = Assets.GetPrefab(prefabTag);
            if (prefab != null)
            {
                LocString locString;
                Sprite sprite;
                if (FxPrefix == FXPrefix.Delivered)
                {
                    // Added too storage
                    locString = global::STRINGS.UI.DELIVERED;
                    sprite = PopFXManager.Instance.sprite_Plus;
                }
                else
                {
                    // Taken from storage
                    locString = global::STRINGS.UI.PICKEDUP;
                    sprite = PopFXManager.Instance.sprite_Negative;
                }

                int amount = (int)ConsumedAmount;
                if(pickupable != null)
                {
                    PrimaryElement component = pickupable.GetComponent<PrimaryElement>();
                    amount = (int)component.Units;
                }

                string text = Assets.IsTagCountable(prefabTag)
                    ? string.Format(locString, amount, prefab.GetProperName())
                    : string.Format(locString, GameUtil.GetFormattedMass(amount), prefab.GetProperName());

                PopFXManager.Instance.SpawnFX(
                    Def.GetUISprite(prefab).first, sprite,
                    text, storage.transform, Vector3.zero);
            }
        }

		public void HandleDiseaseTransfer(GameObject go, Storage storage)
		{
			if (go == null || storage == null) return;

			PrimaryElement primaryElement = storage.primaryElement;
			PrimaryElement component = go.GetComponent<PrimaryElement>();
			if (ShouldTransferDisease(
				    DoDiseaseTransfer, component != null, primaryElement != null))
			{
                SimUtil.DiseaseInfo invalid = SimUtil.DiseaseInfo.Invalid;
                invalid.idx = component.DiseaseIdx;
                invalid.count = (int)((float)component.DiseaseCount * 0.05f);
                SimUtil.DiseaseInfo invalid2 = SimUtil.DiseaseInfo.Invalid;
                invalid2.idx = primaryElement.DiseaseIdx;
                invalid2.count = (int)((float)primaryElement.DiseaseCount * 0.05f);
                component.ModifyDiseaseCount(-invalid.count, "Storage.TransferDiseaseWithObject");
                primaryElement.ModifyDiseaseCount(-invalid2.count, "Storage.TransferDiseaseWithObject");
                if (invalid.count > 0)
                {
                    primaryElement.AddDisease(invalid.idx, invalid.count, "Storage.TransferDiseaseWithObject");
                }

                if (invalid2.count > 0)
                {
                    component.AddDisease(invalid2.idx, invalid2.count, "Storage.TransferDiseaseWithObject");
				}
			}
		}

		internal static bool ShouldTransferDisease(
			bool enabled, bool hasItemPrimary, bool hasStoragePrimary)
			=> enabled && hasItemPrimary && hasStoragePrimary;
	}
}
