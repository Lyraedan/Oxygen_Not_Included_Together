using HarmonyLib;
using ONI_Together.Networking;
using Shared.Profiling;

namespace ONI_Together.Patches.Critters
{
	internal class CreatureTemperaturePatches
	{
		[HarmonyPatch(typeof(CreatureSimTemperatureTransfer), nameof(CreatureSimTemperatureTransfer.unsafeUpdateAverageKiloWattsExchanged))]
		public static class CreatureSimTemperatureTransfer_UpdateAverage_Patch
		{
			public static bool Prefix(CreatureSimTemperatureTransfer __instance)
			{
				using var _ = Profiler.Scope();

				if (MultiplayerSession.IsClient)
					return false;

				return __instance != null && __instance.primaryElement != null &&
					__instance.average_kilowatts_exchanged != null && Game.Instance != null &&
					Game.Instance.simData != null && Sim.IsValidHandle(__instance.simHandle);
			}
		}
	}
}
