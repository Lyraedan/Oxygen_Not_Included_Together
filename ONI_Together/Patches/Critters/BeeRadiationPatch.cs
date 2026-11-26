using HarmonyLib;

namespace ONI_Together.Patches.Critters
{
	/// <summary>
	/// Prevents ArgumentException in Bee.AddRadiationModifier when BeeHappinessMonitor
	/// re-enters the unhappy state and attempts to add an already-present modifier key.
	/// </summary>
	[HarmonyPatch(typeof(Bee), nameof(Bee.AddRadiationModifier))]
	internal static class Bee_AddRadiationModifier_Patch
	{
		static bool Prefix(Bee __instance, HashedString name, float mod)
		{
			if (__instance == null || __instance.radiationModifiers == null)
				return true;

			if (__instance.radiationModifiers.ContainsKey(name))
			{
				__instance.radiationModifiers[name] = mod;
				__instance.RefreshRadiationOutput();
				return false;
			}

			return true;
		}
	}
}
