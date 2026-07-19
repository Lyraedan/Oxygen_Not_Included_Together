using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.OxySync.StateMachines;
using Shared.Profiling;

namespace ONI_Together.Patches.OxySync
{
    [HarmonyPatch(typeof(AlgaeHabitat), nameof(AlgaeHabitat.OnSpawn))]
    public static class AlgaeHabitat_OxySync_Patch
    {
        public static void Postfix(AlgaeHabitat __instance)
        {
            using var _ = Profiler.Scope();

            if (__instance.IsNullOrDestroyed())
                return;

            __instance.gameObject.AddOrGet<AlgaeHabitatSyncer>();
        }
    }
}
