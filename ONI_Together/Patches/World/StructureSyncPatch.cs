using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.Networking.OxySync.StateMachines;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
    [HarmonyPatch(typeof(Battery), nameof(Battery.OnSpawn))]
    public static class BatterySpawnPatch
    {
        public static void Postfix(Battery __instance)
        {
            //BatteryStateSyncer syncer = __instance.gameObject.AddOrGet<BatteryStateSyncer>();
            __instance.gameObject.AddOrGet<BatterySyncComponent>();
        }
    }

    [HarmonyPatch(typeof(Generator), nameof(Generator.OnSpawn))]
    public static class GeneratorSpawnPatch
    {
        public static void Postfix(Generator __instance)
        {
            if (__instance.gameObject.TryGetComponent<EnergyGenerator>(out _))
            {
                //EnergyGeneratorSyncer egenSyncer = __instance.gameObject.AddOrGet<EnergyGeneratorSyncer>();
                __instance.gameObject.AddOrGet<EnergyGeneratorSyncComponent>();
                return;
            }

            //GenericGeneratorSyncer syncer = __instance.gameObject.AddOrGet<GenericGeneratorSyncer>();
            __instance.gameObject.AddOrGet<GenericGeneratorSyncComponent>();
        }
    }

    [HarmonyPatch]
    public static class StorageBuildingPatches
    {
        [HarmonyPostfix]
        public static void Postfix(object __instance)
        {
            using var _ = Profiler.Scope();
            ((KMonoBehaviour) __instance).gameObject.AddOrGet<StorageSyncComponent>();
        }

        [HarmonyTargetMethods]
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            const string name = nameof(KMonoBehaviour.OnSpawn);
            yield return AccessTools.Method(typeof(StorageLocker), name);
            yield return AccessTools.Method(typeof(RationBox), name);
            yield return AccessTools.Method(typeof(CargoBay), name);
            yield return AccessTools.Method(typeof(CargoBayCluster), name);
            //yield return AccessTools.Method(typeof(LiquidReservoir), name); // LiquidReservoir needs its actual class setting here
        }
    }

    /* Not scalable, patch buildings that we want storage syncing on
    [HarmonyPatch(typeof(Storage), nameof(Storage.OnSpawn))]
    public static class StorageLocker_OnSpawn_Patch
    {
        public static void Postfix(Storage __instance)
        {
            using var _ = Profiler.Scope();
            StorageStateSyncer syncer = __instance.gameObject.AddOrGet<StorageStateSyncer>();
        }
    }

    }
    */

    [HarmonyPatch(typeof(FlushToilet), nameof(FlushToilet.OnSpawn))]
    public static class FlushToiletSpawnPatch
    {
        public static void Postfix(FlushToilet __instance)
        {
            using var _ = Profiler.Scope();
            __instance.gameObject.AddOrGet<ToiletStructureSyncer>();
        }
    }

    [HarmonyPatch(typeof(Toilet), nameof(Toilet.OnSpawn))]
    public static class ToiletSpawnPatch
    {
        public static void Postfix(Toilet __instance)
        {
            using var _ = Profiler.Scope();
            __instance.gameObject.AddOrGet<ToiletStructureSyncer>();
        }
    }
    
    [HarmonyPatch(typeof(Reactor), nameof(Reactor.OnSpawn))]
    public static class ReactorSpawnPatch
    {
        public static void Postfix(Reactor __instance)
        {
            using var _ = Profiler.Scope();
            __instance.gameObject.AddOrGet<NuclearReactorSyncer>();
        }
    }

    [HarmonyPatch(typeof(Growing), nameof(Growing.OnSpawn))]
    public static class Growing_OnSpawn_OxySync_Patch
    {
        public static void Postfix(Growing __instance)
        {
            using var _ = Profiler.Scope();
            __instance.gameObject.AddOrGet<PlantSyncComponent>();
        }
    }

    [HarmonyPatch(typeof(Telepad), nameof(Telepad.OnSpawn))]
    public static class TelepadSyncPatch
    {
        public static void Postfix(Telepad __instance)
        {
            using var _ = Profiler.Scope();
            __instance.gameObject.AddOrGet<PrintingPodSyncComponent>();
        }
    }
}
