using System.Collections.Generic;
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
            // No session check: the host loads the save before it starts the server, so every cot
            // that is already in the save would spawn without a syncer and never send its state.
            if (__instance.IsNullOrDestroyed())
                return;
            __instance.gameObject.AddOrGet<ClinicStateSyncer>();
        }
    }

    /// <summary>
    /// The enter/exit actions of the cot's healing states manage the patient's effects, the doctor
    /// chore and the doctored timer. All of that belongs to the host (EffectsPatch blocks effects on
    /// clients, so StartEffect returns null there and doctored's exit throws on it). Clients still
    /// enter the states, they just skip those actions.
    /// </summary>
    [HarmonyPatch(typeof(Clinic.ClinicSM), nameof(Clinic.ClinicSM.InitializeStates))]
    public static class ClinicSM_HealingActions_Patch
    {
        public static void Postfix(Clinic.ClinicSM __instance)
        {
            var healing = __instance.operational.healing;
            foreach (var state in new StateMachine.BaseState[] { healing.undoctored, healing.newlyDoctored, healing.doctored })
            {
                HostOnly(state.enterActions);
                HostOnly(state.exitActions);
            }
        }

        private static void HostOnly(List<StateMachine.Action> actions)
        {
            if (actions == null)
                return;

            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                if (!(action.callback is StateMachine<Clinic.ClinicSM, Clinic.ClinicSM.Instance, Clinic, object>.State.Callback original))
                    continue;

                action.callback = new StateMachine<Clinic.ClinicSM, Clinic.ClinicSM.Instance, Clinic, object>.State.Callback(smi =>
                {
                    if (MultiplayerSession.InActiveSession && MultiplayerSession.IsClient)
                        return;
                    original(smi);
                });
                actions[i] = action;
            }
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
