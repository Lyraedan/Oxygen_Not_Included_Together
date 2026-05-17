using HarmonyLib;
using System.Collections.Generic;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Trackers
{
	public static class MopTracker
	{
		public static readonly HashSet<GameObject> MopPlacers = new HashSet<GameObject>();
		private static readonly Tag MopPlacerTag = new Tag("MopPlacer");

		[HarmonyPatch(typeof(KPrefabID), "OnSpawn")]
		public static class KPrefabID_OnSpawn_Patch
		{
			public static void Postfix(KPrefabID __instance)
			{
				using var _ = Profiler.Scope();

				if (__instance.PrefabTag == MopPlacerTag)
				{
					lock (MopPlacers)
					{
						MopPlacers.Add(__instance.gameObject);
					}
				}
			}
		}

		[HarmonyPatch(typeof(KPrefabID), "OnCleanUp")]
		public static class KPrefabID_OnCleanUp_Patch
		{
			public static void Prefix(KPrefabID __instance)
			{
				using var _ = Profiler.Scope();

				if (__instance.PrefabTag == MopPlacerTag)
				{
					lock (MopPlacers)
					{
						MopPlacers.Remove(__instance.gameObject);
					}
				}
			}
		}
	}
}
