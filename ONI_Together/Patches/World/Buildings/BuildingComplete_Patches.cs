using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using Shared.Profiling;
using ONI_Together.Networking.OxySync.Components;

namespace ONI_Together.Patches.World.Buildings
{
	internal class BuildingComplete_Patches
	{

        [HarmonyPatch(typeof(BuildingComplete), nameof(BuildingComplete.OnPrefabInit))]
        public class BuildingComplete_OnPrefabInit_Patch
        {
            public static void Postfix(BuildingComplete __instance)
            {
                using var _ = Profiler.Scope();
                try
                {
                    __instance.gameObject.AddOrGet<NetworkIdentity>();

                    if (AnimSyncEligibility.IsAnimatedBuilding(__instance.gameObject))
                        __instance.gameObject.AddOrGet<AnimSyncer>();
                }
                catch (System.Exception ex)
                {
                    DebugConsole.LogError($"[BuildingComplete_OnPrefabInit_Patch] {ex}");
                }
            }
        }
	}
}
