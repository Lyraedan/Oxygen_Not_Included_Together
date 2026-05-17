using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;

namespace ONI_Together.Patches.StateMachines
{
    public class StateMachinePatches
    {
        [HarmonyPatch(typeof(StateMachine.Instance), nameof(StateMachine.Instance.IsRunning))]
        public static class StateMachine_IsRunning_Patch
        {
            static bool Prefix(StateMachine.Instance __instance, ref bool __result)
            {
                return true; // disabled
                /*
                if (__instance.IsSMIPaused())
                {
                    __result = false;
                    return false;
                }

                return true; // run original
                */
            }
        }
    }
}
