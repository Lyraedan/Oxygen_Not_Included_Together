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

        [HarmonyPatch(typeof(GunkEmptierWorkable), nameof(GunkEmptierWorkable.TriggerRoomEffects))]
        public static class GunkEmptierWorkable_TriggerRoomEffects_Patch
        {
            static bool Prefix()
            {
                // Effects.Add is host-authoritative and returns null for local client effects.
                // Replay the work normally, but receive room effects through ToggleEffectPacket.
                return !MultiplayerSession.IsClient;
            }
        }

        public static class BionicOilMonitor_OilAmountInstanceWatcherUpdate_Patch
        {
            static bool Prefix()
            {
                // Loaded monitors can retain scheduled updates after client AI is disabled.
                // Oil amounts and effects are synchronized from the host, not simulated locally.
                return !MultiplayerSession.IsClient;
            }

            public static void ExecutePatch()
            {
                // Harmony can run this type's static constructor, which requires an initialized Db.
                var target = AccessTools.Method(typeof(BionicOilMonitor), nameof(BionicOilMonitor.OilAmountInstanceWatcherUpdate));
                var prefix = AccessTools.Method(typeof(BionicOilMonitor_OilAmountInstanceWatcherUpdate_Patch), nameof(Prefix));
                MultiplayerMod.Harmony.Patch(target, new HarmonyMethod(prefix));
            }
        }

        [HarmonyPatch(typeof(BionicOilMonitor.Instance), nameof(BionicOilMonitor.Instance.ReportOilTankFilled))]
        public static class BionicOilMonitor_ReportOilTankFilled_Patch
        {
            static bool Prefix(BionicOilMonitor.Instance __instance)
            {
                if (MultiplayerSession.IsClient || __instance == null)
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
                if (MultiplayerSession.IsClient || __instance == null)
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
                if (MultiplayerSession.IsClient || __instance == null)
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
                if (MultiplayerSession.IsClient || __instance == null)
                    return false;

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
