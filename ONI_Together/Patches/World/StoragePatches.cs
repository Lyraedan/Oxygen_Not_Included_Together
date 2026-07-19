using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DuplicantActions;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World
{
	[System.Flags]
	internal enum StorageReplicationKind
	{
		None = 0,
		Membership = 1,
		CarryVisual = 2,
	}

	public static class StoragePatches
	{
		internal static StorageReplicationKind RequiredReplication(bool isMinionStorage)
			=> StorageReplicationKind.Membership
			   | (isMinionStorage
				   ? StorageReplicationKind.CarryVisual
				   : StorageReplicationKind.None);

		internal static bool ShouldReplicateRemoval(bool itemUnavailableForBinding)
			=> !itemUnavailableForBinding;

        [HarmonyPatch(typeof(Storage), nameof(Storage.Remove))]
        public static class StorageRemovePatch
        {
            public static void Postfix(Storage __instance, GameObject go)
            {
                using var _ = Profiler.Scope();

                if (!MultiplayerSession.IsHost || !MultiplayerSession.InSession) return;
                if (__instance == null || go == null) return;
				var existingItemIdentity = go.GetComponent<NetworkIdentity>();
					if (!ShouldReplicateRemoval(existingItemIdentity?.IsUnavailableForBinding == true)) return;

                var storageIdentity = __instance.GetNetIdentity();
                if (storageIdentity == null || storageIdentity.NetId == 0) return;
                
				bool isMinionStorage = __instance.GetComponent<MinionBrain>() != null;
				StorageReplicationKind replication = RequiredReplication(isMinionStorage);
				var goIdentity = go.GetNetIdentity();
				var primary = go.GetComponent<PrimaryElement>();
				if ((replication & StorageReplicationKind.Membership) != 0)
				{
					PacketSender.SendToAllClients(new StorageItemPacket
					{
						NetId = goIdentity?.NetId ?? 0,
						StorageNetId = storageIdentity.NetId,
						Revision = NetworkIdentityRegistry.NextAuthorityRevision(),
						DoDiseaseTransfer = false,
						FxPrefix = Storage.FXPrefix.PickedUp,
						ConsumedPrefabHash = go.PrefabID().GetHashCode(),
						ConsumedAmount = primary?.Mass ?? 0f
					});
				}

				if ((replication & StorageReplicationKind.CarryVisual) != 0)
				{
					PacketSender.SendToAllClients(new DuplicantCarryItemPacket
					{
						NetId = storageIdentity.NetId,
						PickupableNetId = goIdentity?.NetId ?? 0,
						IsCarrying = false
					});
				}
            }
        }

        // Edible.StopConsuming is called when a dupe finishes eating food.
        // The food GameObject is destroyed directly, bypassing Storage.Remove,
        // so we need a separate patch to clean up the carried item proxy.
        // However, I believe doing it in StartConsuming makes sense since they are removing it from their back
        [HarmonyPatch(typeof(Edible), nameof(Edible.StartConsuming))]
        public static class EdibleStopConsumingPatch
        {
            public static void Postfix(Edible __instance)
            {
                using var _ = Profiler.Scope();

                if (!MultiplayerSession.IsHost || !MultiplayerSession.InSession) return;
                if (__instance == null || __instance.worker == null) return;

                var dupeStorage = __instance.worker.GetComponent<Storage>();
                if (dupeStorage == null) return;

                var storageIdentity = dupeStorage.GetNetIdentity();
                if (storageIdentity == null || storageIdentity.NetId == 0) return;

                var goIdentity = __instance.gameObject.GetNetIdentity();
                int consumedItemNetId = (goIdentity != null) ? goIdentity.NetId : 0;
                PacketSender.SendToAllClients(new DuplicantCarryItemPacket
                {
                    NetId = storageIdentity.NetId,
                    PickupableNetId = consumedItemNetId,
                    IsCarrying = false
                });
            }
        }

        // Pickupable.OnCleanUp only fires when the object is destroyed. Items that are
        // reparented into Storage (seeds into planters, eggs into incubators, live
        // critters, non-stackable items) stay alive and never trigger OnCleanUp, so
        // clients keep rendering them on the ground.
        [HarmonyPatch(typeof(Storage), nameof(Storage.Store), new System.Type[] { typeof(GameObject), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
        public static class StorageStorePatch
        {
            public static void Postfix(Storage __instance, GameObject go, GameObject __result,
                bool hide_popups, bool block_events, bool do_disease_transfer, bool is_deserializing)
            {
                using var _ = Profiler.Scope();
                try
                {
                    if (!MultiplayerSession.IsHost || !MultiplayerSession.InSession)
                        return;
					GameObject authoritativeItem = __result ?? go;
					if (__instance == null || authoritativeItem == null)
                        return;

                    var storageIdentity = __instance.GetNetIdentity();
                    if (storageIdentity == null || storageIdentity.NetId == 0)
                        return;

					var identity = authoritativeItem.GetNetIdentity();
					var pe = authoritativeItem.GetComponent<PrimaryElement>();
                    int itemNetId = (identity != null) ? identity.NetId : 0;
                    
					bool isMinionStorage = __instance.GetComponent<MinionBrain>() != null;
					StorageReplicationKind replication = RequiredReplication(isMinionStorage);
					if ((replication & StorageReplicationKind.Membership) != 0)
					{
						PacketSender.SendToAllClients(new StorageItemPacket
						{
							NetId = itemNetId,
							StorageNetId = storageIdentity.NetId,
							Revision = NetworkIdentityRegistry.NextAuthorityRevision(),
							DoDiseaseTransfer = do_disease_transfer,
							FxPrefix = Storage.FXPrefix.Delivered,
							ConsumedPrefabHash = authoritativeItem.PrefabID().GetHashCode(),
							ConsumedAmount = pe?.Mass ?? 0,
							HasElementState = pe != null,
							ElementMass = pe?.Mass ?? 0,
							ElementTemperature = pe?.Temperature ?? 0,
							ElementDiseaseIdx = pe?.DiseaseIdx ?? byte.MaxValue,
							ElementDiseaseCount = pe?.DiseaseCount ?? 0
						});
					}

					// Carry visuals are additive to authoritative Storage.items membership.
					if ((replication & StorageReplicationKind.CarryVisual) != 0)
					{
						var itemAnimCtrl = authoritativeItem.GetComponentInChildren<KBatchedAnimController>();
                        var animFile = itemAnimCtrl?.AnimFiles?[0]?.name;
                        if (animFile != null)
                        {
                            PacketSender.SendToAllClients(new DuplicantCarryItemPacket
                            {
                                NetId = storageIdentity.NetId,
                                PickupableNetId = itemNetId,
                                AnimFileName = animFile,
                                ItemPrefabHash = authoritativeItem.PrefabID().GetHashCode(),
                                IsCarrying = true
                            });
                        }
                    }
				}
                catch (System.Exception ex)
                {
                    DebugConsole.LogError($"[StorageStorePatch] Exception: {ex}");
                }
            }
        }
    }
}
