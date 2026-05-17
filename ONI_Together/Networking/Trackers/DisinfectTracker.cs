using HarmonyLib;
using System.Collections.Generic;
using Shared.Profiling;

namespace ONI_Together.Networking.Trackers
{
	public static class DisinfectTracker
	{
		public static readonly HashSet<Disinfectable> Disinfectables = new HashSet<Disinfectable>();

		// We patch KPrefabID because Disinfectable might not override OnSpawn/OnCleanUp,
		// causing "Undefined target method" crashes if we try to patch Disinfectable directly.
		[HarmonyPatch(typeof(KPrefabID), "OnSpawn")]
		public static class Disinfectable_OnSpawn_Patch
		{
			public static void Postfix(KPrefabID __instance)
			{
				using var _ = Profiler.Scope();

				var disinfectable = __instance.GetComponent<Disinfectable>();
				if (disinfectable != null)
				{
					lock (Disinfectables)
					{
						Disinfectables.Add(disinfectable);
					}
				}
			}
		}

		[HarmonyPatch(typeof(KPrefabID), "OnCleanUp")]
		public static class Disinfectable_OnCleanUp_Patch
		{
			public static void Prefix(KPrefabID __instance)
			{
				using var _ = Profiler.Scope();

				var disinfectable = __instance.GetComponent<Disinfectable>();
				if (disinfectable != null)
				{
					lock (Disinfectables)
					{
						Disinfectables.Remove(disinfectable);
					}
				}
			}
		}
	}
}
