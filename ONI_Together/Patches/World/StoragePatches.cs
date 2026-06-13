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
	public static class StoragePatches
	{
        [HarmonyPatch(typeof(Storage), nameof(Storage.Remove))]
        public static class StorageRemovePatch
        {
            public static void Postfix(Storage __instance, GameObject go)
            {
                using var _ = Profiler.Scope();

                if (!MultiplayerSession.IsHost || !MultiplayerSession.InSession) return;
                if (__instance == null || go == null) return;

                var storageIdentity = __instance.GetNetIdentity();
                if (storageIdentity == null || storageIdentity.NetId == 0) return;

                var pe = go.GetComponent<PrimaryElement>();
                PacketSender.SendToAllClients(new StorageItemPacket
                {
                    NetId = 0, // FX Only
                    StorageNetId = storageIdentity.NetId,
                    DoDiseaseTransfer = false,
                    FxPrefix = Storage.FXPrefix.PickedUp,
                    ConsumedPrefabHash = go.PrefabID().GetHashCode(),
                    ConsumedAmount = pe?.Mass ?? 0f
                });

                // If this is a duplicant's storage, notify clients to remove the carried item visual
                if (__instance.GetComponent<MinionBrain>() != null)
                {
                    var goIdentity = go.GetNetIdentity();
                    if (goIdentity != null && goIdentity.NetId != 0)
                    {
                        DebugConsole.Log($"[StorageRemovePatch] Sending put-down packet: dupe={storageIdentity.NetId} item={goIdentity.NetId} prefab={go.PrefabID()}");
                        PacketSender.SendToAllClients(new DuplicantCarryItemPacket
                        {
                            NetId = storageIdentity.NetId,
                            PickupableNetId = goIdentity.NetId,
                            IsCarrying = false
                        });
                    }
                }
            }
        }

        // Pickupable.OnCleanUp only fires when the object is destroyed. Items that are
        // reparented into Storage (seeds into planters, eggs into incubators, live
        // critters, non-stackable items) stay alive and never trigger OnCleanUp, so
        // clients keep rendering them on the ground.
        [HarmonyPatch(typeof(Storage), nameof(Storage.Store), new System.Type[] { typeof(GameObject), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
        public static class StorageStorePatch
        {
            public static void Postfix(Storage __instance, GameObject go, bool hide_popups, bool block_events, bool do_disease_transfer, bool is_deserializing)
            {
                using var _ = Profiler.Scope();
                try
                {
                    if (!MultiplayerSession.IsHost || !MultiplayerSession.InSession)
                        return;
                    if (go == null)
                        return;

                    var storageIdentity = __instance.GetNetIdentity();
                    if (storageIdentity == null || storageIdentity.NetId == 0)
                        return;

                    var identity = go.GetNetIdentity();
                    var pe = go.GetComponent<PrimaryElement>();

                    if (identity != null && identity.NetId != 0)
                    {
                        PacketSender.SendToAllClients(new StorageItemPacket
                        {
                            NetId = identity.NetId,
                            StorageNetId = storageIdentity.NetId,
                            DoDiseaseTransfer = do_disease_transfer,
                            FxPrefix = Storage.FXPrefix.Delivered,
                            ConsumedPrefabHash = go.PrefabID().GetHashCode(),
                            ConsumedAmount = pe?.Mass ?? 0
                        });

                        // If this is a duplicant's storage, notify clients to show the item on their back
                        if (__instance.GetComponent<MinionBrain>() != null)
                        {
                            var itemAnimCtrl = go.GetComponentInChildren<KBatchedAnimController>();
                            var animFile = itemAnimCtrl?.AnimFiles?[0]?.name;
                            if (animFile != null)
                            {
                                //DebugConsole.Log($"[StorageStorePatch] Sending carry packet: dupe={storageIdentity.NetId} item={go.PrefabID()} anim={animFile}");
                                PacketSender.SendToAllClients(new DuplicantCarryItemPacket
                                {
                                    NetId = storageIdentity.NetId,
                                    PickupableNetId = identity.NetId,
                                    AnimFileName = animFile,
                                    ItemPrefabHash = go.PrefabID().GetHashCode(),
                                    IsCarrying = true
                                });
                            }
                            /*else
                            {
                                DebugConsole.LogWarning($"[StorageStorePatch] Item {go.PrefabID()} has no anim file (ctrl={itemAnimCtrl != null})");
                            }*/
                        }
                    }
                    else
                    {
                        PacketSender.SendToAllClients(new StorageItemPacket
                        {
                            NetId = 0, // FX Only
                            StorageNetId = storageIdentity.NetId,
                            DoDiseaseTransfer = false,
                            FxPrefix = Storage.FXPrefix.Delivered,
                            ConsumedPrefabHash = go.PrefabID().GetHashCode(),
                            ConsumedAmount = pe?.Mass ?? 0
                        });
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
