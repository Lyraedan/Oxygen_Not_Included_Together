using HarmonyLib;
using System.Collections.Generic;
using Shared.Profiling;

namespace ONI_Together.Networking.Trackers
{
	public static class PlantTracker
	{
		public static readonly HashSet<Growing> AllPlants = new HashSet<Growing>();

		[HarmonyPatch(typeof(Growing), nameof(Growing.OnSpawn))]
		public static class Growing_OnSpawn_Patch
		{
			public static void Postfix(Growing __instance)
			{
				using var _ = Profiler.Scope();

				lock (AllPlants)
				{
					AllPlants.Add(__instance);
				}
			}
		}

		[HarmonyPatch(typeof(KPrefabID), nameof(KPrefabID.OnCleanUp))]
		public static class KPrefabID_OnCleanUp_PlantTracker_Patch
		{
			public static void Prefix(KPrefabID __instance)
			{
				using var _ = Profiler.Scope();

				var growing = __instance.GetComponent<Growing>();
				if (growing != null)
				{
					lock (AllPlants)
					{
						AllPlants.Remove(growing);
					}
				}
			}
		}
	}
}
