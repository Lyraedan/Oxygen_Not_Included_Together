using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Components;
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
				if (!PlantLifecycleSyncComponent.CanBroadcast)
					return;
				if (__result == null || PlantLifecycleSyncComponent.IsApplyingState)
					return;
				if (!__result.TryGetComponent<Growing>(out var growing) || growing == null)
					return;

				PlantLifecycleSyncComponent.Instance?.BroadcastSpawn(growing, __instance);
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
				if (!PlantLifecycleSyncComponent.CanBroadcast)
					return;
				if (PlantLifecycleSyncComponent.IsApplyingState || __instance == null)
					return;
				if (!__instance.IsWildPlanted())
					return;

				PlantLifecycleSyncComponent.Instance?.BroadcastSpawn(__instance);
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
				if (!PlantLifecycleSyncComponent.CanBroadcast)
					return;
				if (PlantLifecycleSyncComponent.IsApplyingState || __instance == null)
					return;

				var growing = __instance.GetComponent<Growing>();
				if (growing == null)
					return;

				PlantLifecycleSyncComponent.Instance?.BroadcastRemove(growing);
			}
		}
	}
}
