using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using ONI_Together.Menus;
using ONI_Together.Networking;

namespace ONI_Together.Patches.World
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

    [HarmonyPatch(typeof(SuitTank), nameof(SuitTank.ConsumeGas))]
    public static class SuitTank_ConsumeGas_Patch
    {
        public static bool Prefix(SuitTank __instance, ref bool __result)
        {
            __result = true;

            if (__instance == null)
            {
                __result = false;
                return false;
            }

            if (MultiplayerSession.IsClient)
            {
                __result = false;
                return false;
            }

            return __result;
        }
    }
}
