using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World.Plants
{
	[HarmonyPatch]
	internal static class PlantLifecyclePatches
	{
		[HarmonyPatch(typeof(PlantablePlot), nameof(PlantablePlot.SpawnOccupyingObject))]
		private static class PlantablePlot_SpawnOccupyingObject_Patch
		{
			private static void Postfix(PlantablePlot __instance, GameObject __result)
			{
				using var _ = Profiler.Scope();

				if (!MultiplayerSession.IsHostInSession)
					return;
				if (!PlantGrowthSyncer.CanBroadcastLifecycleEvents)
					return;
				if (__result == null || PlantGrowthSyncer.IsApplyingState)
					return;
				if (!__result.TryGetComponent<Growing>(out var growing) || growing == null)
					return;

				PlantGrowthSyncer.BroadcastPlantLifecycle(PlantLifecycleOperation.Spawn, growing, __instance);
			}
		}

		[HarmonyPatch(typeof(Growing), nameof(Growing.OnSpawn))]
		private static class Growing_OnSpawn_Patch
		{
			private static void Postfix(Growing __instance)
			{
				using var _ = Profiler.Scope();

				if (!MultiplayerSession.IsHostInSession)
					return;
				if (!PlantGrowthSyncer.CanBroadcastLifecycleEvents)
					return;
				if (PlantGrowthSyncer.IsApplyingState || __instance == null)
					return;
				if (!__instance.IsWildPlanted())
					return;

				PlantGrowthSyncer.BroadcastPlantLifecycle(PlantLifecycleOperation.Spawn, __instance);
			}
		}

		[HarmonyPatch(typeof(KPrefabID), nameof(KPrefabID.OnCleanUp))]
		private static class KPrefabID_OnCleanUp_Patch
		{
			private static void Prefix(KPrefabID __instance)
			{
				using var _ = Profiler.Scope();

				if (!MultiplayerSession.IsHostInSession)
					return;
				if (!PlantGrowthSyncer.CanBroadcastLifecycleEvents)
					return;
				if (PlantGrowthSyncer.IsApplyingState || __instance == null)
					return;

				var growing = __instance.GetComponent<Growing>();
				if (growing == null)
					return;

				PlantGrowthSyncer.BroadcastPlantLifecycle(PlantLifecycleOperation.Remove, growing);
			}
		}
	}
}
