using HarmonyLib;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Components;

namespace ONI_Together.Patches.OxySync
{
    [HarmonyPatch(typeof(NetworkIdentity), nameof(NetworkIdentity.OnSpawn))]
    public static class StatusItemGroupSyncPatch
    {
        public static void Postfix(NetworkIdentity __instance)
        {
            if (__instance == null || !__instance.TryGetComponent<KSelectable>(out _))
                return;

            var syncer = __instance.gameObject.AddOrGet<StatusItemsSyncer>();
            syncer.recieverType = ResolveReceiverType(__instance.gameObject);
        }

        internal static StatusItemsSyncer.StatusRecieverType ResolveReceiverType(UnityEngine.GameObject go)
        {
            if (go.GetComponent<RoverModifiers>() != null)
                return StatusItemsSyncer.StatusRecieverType.ROBOT;
            if (go.HasTag(GameTags.BaseMinion))
                return StatusItemsSyncer.StatusRecieverType.DUPLICANT;
            if (go.HasTag(GameTags.Creature))
                return StatusItemsSyncer.StatusRecieverType.CREATURE;
            if (go.GetComponent<Building>() != null)
                return StatusItemsSyncer.StatusRecieverType.BUILDING;
            return StatusItemsSyncer.StatusRecieverType.MISC;
        }
    }
}
