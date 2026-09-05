using HarmonyLib;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
	internal class RocketPatches
	{
		[HarmonyPatch(typeof(LaunchableRocketCluster.StatesInstance), nameof(LaunchableRocketCluster.StatesInstance.IsNotGroundBound))]
		public static class LaunchableRocketCluster_IsNotGroundBound_Patch
		{
			public static bool Prefix(LaunchableRocketCluster.StatesInstance __instance, ref bool __result)
			{
				using var _ = Profiler.Scope();

				var module = __instance?.GetComponent<RocketModuleCluster>();
				var craftInterface = module?.CraftInterface;
				var craft = craftInterface?.GetComponent<Clustercraft>();
				if (module != null && craftInterface != null && craft != null)
					return true;

				__result = false;
				return false;
			}
		}
	}
}
