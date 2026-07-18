using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.StateMachines;
using Shared.Profiling;

namespace ONI_Together.Patches.OxySync
{

    [HarmonyPatch(typeof(Grave), nameof(Grave.OnSpawn))]
    public static class GraveSpawnPatch
    {
        public static void Postfix(Grave __instance)
        {
            using var _ = Profiler.Scope();

            if (__instance.IsNullOrDestroyed())
                return;

            // The grave doesn't have its own network identity, so we need to register one ourselves.
            __instance.gameObject.AddOrGet<NetworkIdentity>().RegisterIdentity();
            __instance.gameObject.AddOrGet<GraveSyncer>();
        }
    }
}
