using HarmonyLib;
using ONI_Together.Networking.OxySync.StateMachines;
using Shared.Profiling;

namespace ONI_Together.Patches.OxySync
{
    [HarmonyPatch(typeof(Electrolyzer), nameof(Electrolyzer.OnSpawn))]
    public static class Electrolyzer_OxySync_Patch
    {
        public static void Postfix(Electrolyzer __instance)
        {
            using var _ = Profiler.Scope();

            if (__instance.IsNullOrDestroyed())
                return;

            __instance.gameObject.AddOrGet<ElectrolyzerStateSyncer>();
        }
    }
}