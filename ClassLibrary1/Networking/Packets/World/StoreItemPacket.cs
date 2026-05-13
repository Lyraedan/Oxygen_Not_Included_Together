using ONI_MP.DebugTools;
using ONI_MP.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;
using UnityEngine;
using static Storage;
using Klei;
using Shared.Interfaces.Networking;

namespace ONI_MP.Networking.Packets.World
{
    /// <summary>
    /// Modified version of GroundItemPickedUpPacket
    /// </summary>
    public class StoreItemPacket : IPacket, IBulkablePacket
    {
        private static readonly HashSet<int> PendingPickupNetIds = [];

        public int NetId;
        public int StorageNetId;
        public FXPrefix FxPrefix;
        public bool DoDiseaseTransfer;

        public int MaxPackSize => 500;

        public uint IntervalMs => 250;

        public static bool TryConsumePending(int netId)
        {
            using var _ = Profiler.Scope();
            return PendingPickupNetIds.Remove(netId);
        }

        public static void ClearPending()
        {
            using var _ = Profiler.Scope();
            int n = PendingPickupNetIds.Count;
            PendingPickupNetIds.Clear();
            DebugConsole.Log($"[PendingPickup] cleared count={n}");
        }

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();
            writer.Write(NetId);
            writer.Write(StorageNetId);
            writer.Write((int)FxPrefix);
            writer.Write(DoDiseaseTransfer);
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();
            NetId = reader.ReadInt32();
            StorageNetId = reader.ReadInt32();
            FxPrefix = (FXPrefix)reader.ReadInt32();
            DoDiseaseTransfer = reader.ReadBoolean();
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (!NetworkIdentityRegistry.TryGetComponent<Pickupable>(NetId, out var pickupable))
            {
                PendingPickupNetIds.Add(NetId);
                DebugConsole.LogWarning($"[StoreItemPacket] Pickupable NetId {NetId} not yet registered; queued pending removal");
                return;
            }

            if (!NetworkIdentityRegistry.TryGetComponent<Storage>(StorageNetId, out var storage))
            {
                DebugConsole.LogWarning($"[StoreItemPacket] No storage found with NetID: {StorageNetId}");
                Util.KDestroyGameObject(pickupable.gameObject); // Still destroy the pickupable
                return;
            }

            DisplayFX(pickupable.gameObject, storage);
            HandleDiseaseTransfer(pickupable.gameObject, storage);
            Util.KDestroyGameObject(pickupable.gameObject);
        }

        public void DisplayFX(GameObject go, Storage storage)
        {
            if (PopFXManager.Instance == null)
                return;

            PrimaryElement component = go.GetComponent<PrimaryElement>();

            LocString locString;
            Transform target_transform;
            if (FxPrefix == FXPrefix.Delivered)
            {
                locString = global::STRINGS.UI.DELIVERED;
                target_transform = storage.transform;
            }
            else
            {
                locString = global::STRINGS.UI.PICKEDUP;
                target_transform = go.transform;
            }

            Vector3 offset = Vector3.zero;
            string text = (Assets.IsTagCountable(go.PrefabID()) ? string.Format(locString, (int)component.Units, go.GetProperName()) : string.Format(locString, GameUtil.GetFormattedMass(component.Units), go.GetProperName()));
            PopFXManager.Instance.SpawnFX(Def.GetUISprite(go).first, (FxPrefix == FXPrefix.Delivered) ? PopFXManager.Instance.sprite_Plus : PopFXManager.Instance.sprite_Negative, text, target_transform, offset);
        }

        public void HandleDiseaseTransfer(GameObject go, Storage storage)
        {
            if(!DoDiseaseTransfer) return;
            if (go == null || storage == null) return;

            PrimaryElement primaryElement = storage.primaryElement;
            PrimaryElement component = go.GetComponent<PrimaryElement>();
            if(!(component == null))
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
    }
}
