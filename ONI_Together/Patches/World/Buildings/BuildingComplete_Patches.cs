using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

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
                        __instance.gameObject.AddOrGet<AnimStateSyncer>();
                }
                catch (System.Exception ex)
                {
                    DebugConsole.LogError($"[BuildingComplete_OnPrefabInit_Patch] {ex}");
                }
            }
        }
	}
}
