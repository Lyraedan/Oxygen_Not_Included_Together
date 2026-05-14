using System;
using System.Collections.Generic;
using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Patches.World
{
	// Adds NetworkIdentity to buildings that need it for BuildingConfigPacket or other interactions
	// Adds NetworkIdentity to buildings that need it
	[HarmonyPatch(typeof(Building), "OnSpawn")]
	public static class BuildingSpawnPatch
	{
        private static readonly List<Type> IdentityRequiredComponents = new()
        {
            typeof(LogicSwitch),
            typeof(Valve),
            typeof(IThresholdSwitch),
            typeof(IActivationRangeTarget),
            typeof(ISliderControl),
            typeof(ISingleSliderControl),
            typeof(ICheckboxControl),
            typeof(IUserControlledCapacity),
            typeof(ISidescreenButtonControl),
            typeof(Door),
            typeof(LimitValve),
            typeof(Compost),
            typeof(StorageLocker),
            typeof(Refrigerator),
            typeof(RationBox)
        };

        public static void Postfix(Building __instance)
		{
			using var _ = Profiler.Scope();
			try
			{
                HandlePostfix(__instance);
            }
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[BuildingSpawnPatch] {ex}");
			}
		}

        private static void HandlePostfix(Building building)
        {
            if (building is not BuildingComplete)
                return;

            var go = building.gameObject;

            if (!RequiresNetworkIdentity(go))
                return;

            go.AddOrGet<NetworkIdentity>().RegisterIdentity();
        }

        private static bool RequiresNetworkIdentity(GameObject go)
        {
            if (AnimSyncEligibility.IsAnimatedBuilding(go))
                return true;

            foreach (var type in IdentityRequiredComponents)
            {
                if (go.GetComponent(type) != null)
                    return true;
            }

            return false;
        }
    }
}
