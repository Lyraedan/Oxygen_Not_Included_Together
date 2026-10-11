using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Misc;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World
{
	[HarmonyPatch(typeof(Scenario), nameof(Scenario.SpawnPrefab), [typeof(int), typeof(int), typeof(int), typeof(string), typeof(Grid.SceneLayer)])]
	public static class ScenarioSpawnPrefabPatch
	{
		public static void Postfix(GameObject __result)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHostInSession || __result == null)
				return;

			// SpawnPrefab returns the object before it is activated: the callers (butcher and death
			// drops, molts, rot piles, meteor resources, lockers, templates) call SetActive(true) right
			// after. Sending activeSelf here left clients with an inactive copy that never initializes.
			SpawnUtils.BroadcastSpawn(__result, isActive: true);
		}
	}
}
