using System;
using Klei.AI;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.StateMachines
{
    public static class SafeStateMachine
    {
        public static bool TryGoTo(this StateMachine.Instance smi, StateMachine.BaseState target)
        {
            if (smi.IsNullOrDestroyed() || target == null)
                return false;
            try
            {
                smi.GoTo(target);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SafeSM] GoTo({target.name}) suppressed: {ex.Message}");
                StateMachine.Instance.error = false;
                return false;
            }
        }

        public static bool TryStopSM(this StateMachine.Instance smi, string reason)
        {
            if (smi.IsNullOrDestroyed())
                return false;
            try
            {
                smi.StopSM(reason);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SafeSM] StopSM({reason}) suppressed: {ex.Message}");
                StateMachine.Instance.error = false;
                try
                {
                    smi.StopSM(reason + "-retry");
                    return true;
                }
                catch (Exception ex2)
                {
                    Debug.LogWarning($"[SafeSM] StopSM({reason}) retry failed: {ex2.Message}");
                    StateMachine.Instance.error = false;
                    return false;
                }
            }
        }

        public static void SafeToggleTag(this StateMachine.Instance smi, Tag tag, bool add)
        {
            if (smi.IsNullOrDestroyed())
                return;
            var master = smi.GetMaster();
            if (master.IsNullOrDestroyed())
                return;
            var kpid = master.GetComponent<KPrefabID>();
            if (kpid.IsNullOrDestroyed())
                return;
            try
            {
                if (add) kpid.AddTag(tag);
                else kpid.RemoveTag(tag);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SafeSM] ToggleTag({tag}, {add}) suppressed: {ex.Message}");
            }
        }

        public static Effects SafeWorkerEffects(this Workable workable)
        {
            if (workable.IsNullOrDestroyed() || workable.worker.IsNullOrDestroyed())
                return null;
            return workable.worker.GetComponent<Effects>();
        }

        public static void ClearGlobalError()
        {
            StateMachine.Instance.error = false;
        }
    }
}
