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
			if (__instance == null || __instance.gameObject == null
				|| !__instance.TryGetComponent<KSelectable>(out _)
				|| !__instance.TryGetComponent<KPrefabID>(out var prefabId)
				|| prefabId == null)
                return;

            var syncer = __instance.gameObject.AddOrGet<StatusItemsSyncer>();
			syncer.recieverType = ResolveReceiverType(__instance.gameObject, prefabId);
        }

        internal static StatusItemsSyncer.StatusRecieverType ResolveReceiverType(UnityEngine.GameObject go)
		{
			return ResolveReceiverType(go, go != null ? go.GetComponent<KPrefabID>() : null);
		}

		private static StatusItemsSyncer.StatusRecieverType ResolveReceiverType(
			UnityEngine.GameObject go, KPrefabID prefabId)
        {
			if (go == null)
				return StatusItemsSyncer.StatusRecieverType.MISC;
            if (go.GetComponent<RoverModifiers>() != null)
                return StatusItemsSyncer.StatusRecieverType.ROBOT;
			if (prefabId != null && prefabId.HasTag(GameTags.BaseMinion))
                return StatusItemsSyncer.StatusRecieverType.DUPLICANT;
			if (prefabId != null && prefabId.HasTag(GameTags.Creature))
                return StatusItemsSyncer.StatusRecieverType.CREATURE;
            if (go.GetComponent<Building>() != null)
                return StatusItemsSyncer.StatusRecieverType.BUILDING;
            return StatusItemsSyncer.StatusRecieverType.MISC;
        }
    }
}
