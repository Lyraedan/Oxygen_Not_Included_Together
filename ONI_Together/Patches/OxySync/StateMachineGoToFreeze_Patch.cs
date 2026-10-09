using HarmonyLib;
using Shared.OxySync;

namespace ONI_Together.Patches.OxySync
{
    [HarmonyPatch(typeof(StateMachine.Instance), nameof(StateMachine.Instance.GoTo), typeof(string))]
    public static class StateMachineGoToFreeze_Patch
    {
        public static bool Prefix(StateMachine.Instance __instance, string state_name)
        {
            return !FrozenStateMachineTracker.IsFrozen(__instance);
        }
    }
}
