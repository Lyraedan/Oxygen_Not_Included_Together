using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.OxySync.StateMachines;
using Shared.Profiling;

namespace ONI_Together.Patches.OxySync
{
    [HarmonyPatch(typeof(ComplexFabricator), nameof(ComplexFabricator.OnSpawn))]
    public static class ComplexFabricatorSpawnPatch
    {
        public static void Postfix(ComplexFabricator __instance)
        {
            using var _ = Profiler.Scope();

            if (__instance.IsNullOrDestroyed())
                return;

            if (__instance.GetSMI<FoodSmoker.StatesInstance>() != null)
                __instance.gameObject.AddOrGet<FoodSmokerSyncer>();
        }
    }
}
