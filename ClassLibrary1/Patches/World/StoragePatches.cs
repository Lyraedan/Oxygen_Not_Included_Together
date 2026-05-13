using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.World;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Patches.World
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
                            FxPrefix = Storage.FXPrefix.PickedUp,
                            ConsumedPrefabHash = go.PrefabID().GetHashCode(),
                            ConsumedAmount = pe?.Mass ?? 0
                        });
                    }
                    else
                    {
                        PacketSender.SendToAllClients(new StorageItemPacket
                        {
                            NetId = 0, // FX Only
                            StorageNetId = storageIdentity.NetId,
                            DoDiseaseTransfer = false,
                            FxPrefix = Storage.FXPrefix.PickedUp,
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
