using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.OxySync.Components;
using Shared.Profiling;

namespace ONI_Together.Patches.OxySync
{
    [HarmonyPatch(typeof(BottleEmptier), nameof(BottleEmptier.OnSpawn))]
    public static class BottleEmptierSpawnPatch
    {
        public static void Postfix(BottleEmptier __instance)
        {
            using var _ = Profiler.Scope();

            if (!MultiplayerSession.InSession)
                return;

            if (__instance.IsNullOrDestroyed())
                return;

            __instance.gameObject.AddOrGet<BottleEmptierSyncer>();
        }
    }
}
