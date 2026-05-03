using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;

namespace ONI_MP.Patches.World
{
    [HarmonyPatch(typeof(SuitTank), nameof(SuitTank.GetTankAmount))]
    public static class SuitTank_GetTankAmount_Patch
    {
        // For the sake of the dupes lives (and notifications on clients) default to full (Atmo Suit)
        private const float DEFAULT_FULL_TANK = 75f;

        public static bool Prefix(SuitTank __instance, ref float __result)
        {
            // If the object itself is invalid, just return 0
            if (__instance == null)
            {
                __result = DEFAULT_FULL_TANK;
                return false;
            }

            if (__instance.storage == null)
            {
                __instance.storage = __instance.GetComponent<Storage>();

                if (__instance.storage == null)
                {
                    __result = DEFAULT_FULL_TANK;
                    return false;
                }
            }

            __result = __instance.storage.GetMassAvailable(__instance.elementTag);
            return false; // skip original
        }
    }
}
