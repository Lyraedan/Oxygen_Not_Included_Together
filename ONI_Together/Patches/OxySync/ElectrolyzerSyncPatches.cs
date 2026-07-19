using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.OxySync.StateMachines;

namespace ONI_Together.Patches.OxySync
{
    [HarmonyPatch(typeof(Electrolyzer), nameof(Electrolyzer.OnSpawn))]
    public static class Electrolyzer_OxySync_Patch
    {
        public static void Postfix(Electrolyzer __instance)
        {
            if (__instance.IsNullOrDestroyed())
                return;

            __instance.gameObject.AddOrGet<ElectrolyzerSyncer>();
        }
    }
}
