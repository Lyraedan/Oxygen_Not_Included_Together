using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.OxySync.StateMachines;
using UnityEngine;

namespace ONI_Together.Patches.OxySync
{
    [HarmonyPatch(typeof(Clinic), nameof(Clinic.OnSpawn))]
    public static class Clinic_OxySync_Patch
    {
        public static void Postfix(Clinic __instance)
        {
            if (!MultiplayerSession.InActiveSession)
                return;
            if (__instance.IsNullOrDestroyed())
                return;
            __instance.gameObject.AddOrGet<ClinicStateSyncer>();
        }
    }

    [HarmonyPatch(typeof(StateMachine.Instance), nameof(StateMachine.Instance.Error))]
    public static class StateMachine_Error_Patch
    {
        public static void Postfix()
        {
            StateMachine.Instance.error = false;
        }
    }
}
