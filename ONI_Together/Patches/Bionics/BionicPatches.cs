using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Klei.AI;
using ONI_Together.Menus;
using ONI_Together.Networking;

namespace ONI_Together.Patches.Bionics
{
    internal class BionicPatches
    {
        // Crude bionic patches, TODO ensure this gets synced properly

        [HarmonyPatch(typeof(BionicOilMonitor.Instance), nameof(BionicOilMonitor.Instance.ReportOilTankFilled))]
        public static class BionicOilMonitor_ReportOilTankFilled_Patch
        {
            static bool Prefix(BionicOilMonitor.Instance __instance)
            {
                if (__instance == null)
                    return false;

                if (__instance.sm == null || __instance.sm.OilFilledSignal == null)
                    return false;

                if (__instance.gameObject == null)
                    return false;

                return true;
            }
        }

        [HarmonyPatch(typeof(BionicOilMonitor.Instance), nameof(BionicOilMonitor.Instance.ReportOilRanOut))]
        public static class BionicOilMonitor_ReportOilRanOut_Patch
        {
            static bool Prefix(BionicOilMonitor.Instance __instance)
            {
                if (__instance == null)
                    return false;

                if (__instance.sm == null || __instance.sm.OilRanOutSignal == null)
                    return false;

                if (__instance.gameObject == null)
                    return false;

                return true;
            }
        }

        [HarmonyPatch(typeof(BionicOilMonitor.Instance), nameof(BionicOilMonitor.Instance.ReportOilValueChanged))]
        public static class BionicOilMonitor_ReportOilValueChanged_Patch
        {
            static bool Prefix(BionicOilMonitor.Instance __instance, float delta)
            {
                if (__instance == null)
                    return false;

                if (__instance.sm == null || __instance.sm.OilValueChanged == null)
                    return false;

                if (__instance.gameObject == null)
                    return false;

                if (__instance.OnOilValueChanged == null) return false;

                return true;
            }
        }

        [HarmonyPatch(typeof(BionicOilMonitor.Instance), nameof(BionicOilMonitor.Instance.StartSM))]
        public static class BionicOilMonitor_Instance_StartSM_Patch
        {
            static bool Prefix(BionicOilMonitor.Instance __instance)
            {
                if (__instance == null)
                    return false;

                // This is what crashes in noOil.Enter
                if (__instance.effects == null)
                    return false;

                if (__instance.resume == null)
                    return false;

                if (__instance.gameObject == null)
                    return false;

                return true; // allow SM start
            }
        }

        [HarmonyPatch(typeof(BionicOilMonitor.Instance), nameof(BionicOilMonitor.Instance.GetEffect))]
        public static class BionicOilMonitor_Instance_GetEffect_Patch
        {
            static bool Prefix(BionicOilMonitor.Instance __instance, ref string __result)
            {
                if (__instance?.resume == null)
                {
                    __result = "NoLubricationMajor";
                    return false;
                }
                return true;
            }
        }
    }
}
