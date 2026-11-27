using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using Shared.Profiling;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ONI_Together.Patches.World
{
    // Registers logic/automation buildings with LogicStateSyncer on spawn
    // and ensures they have a NetworkIdentity for packet routing.
    [HarmonyPatch(typeof(Building), nameof(Building.OnSpawn))]
    public static class LogicBuildingSpawnRegistrationPatch
    {
        private static readonly List<Type> LogicComponentTypes = new()
        {
            typeof(Switch),
            typeof(LogicGate),
            typeof(LogicMemory),
            typeof(LogicRibbonReader),
            typeof(LogicRibbonWriter),
            typeof(Automatable),
        };

        public static void Postfix(Building __instance)
        {
            using var _ = Profiler.Scope();

            try
            {
                if (__instance is not BuildingComplete)
                    return;

                var go = __instance.gameObject;
                if (!HasLogicComponent(go))
                    return;

                go.AddOrGet<NetworkIdentity>().RegisterIdentity();
                LogicStateSyncer.Instance?.Register(go);

                DebugConsole.Log($"[LogicBuildingRegistration] Registered {go.name} with LogicStateSyncer");
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[LogicBuildingRegistration] {ex}");
            }
        }

        private static bool HasLogicComponent(GameObject go)
        {
            foreach (var type in LogicComponentTypes)
            {
                if (go.GetComponent(type) != null)
                    return true;
            }
            return false;
        }
    }

    // Unregister from LogicStateSyncer when buildings are destroyed
    [HarmonyPatch(typeof(Building), nameof(Building.OnCleanUp))]
    public static class LogicBuildingCleanupRegistrationPatch
    {
        public static void Postfix(Building __instance)
        {
            using var _ = Profiler.Scope();

            try
            {
                if (__instance == null || __instance.gameObject == null)
                    return;

                LogicStateSyncer.Instance?.Unregister(__instance.gameObject);
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[LogicBuildingCleanup] {ex}");
            }
        }
    }
}
