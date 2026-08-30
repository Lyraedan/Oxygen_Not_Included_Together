using HarmonyLib;
using ONI_Together.Networking.OxySync.StateMachines;
using Shared.Profiling;

namespace ONI_Together.Patches.OxySync
{

    [HarmonyPatch(typeof(RustDeoxidizer), nameof(RustDeoxidizer.OnSpawn))]
    public static class RustDeoxidizer_OxySync_Patch
    {
        public static void Postfix(RustDeoxidizer __instance)
        {
            using var _ = Profiler.Scope();

            if (__instance.IsNullOrDestroyed())
                return;

            __instance.gameObject.AddOrGet<RustDeoxidizerStateSyncer>();
        }
    }
}
