using KSerialization;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.StateMachines
{
    [SkipSaveFileSerialization]
    public class ClinicStateSyncer : StateMachineSyncer
    {
        private Clinic _clinic;
        private Clinic.ClinicSM.Instance _smi;

        public override void OnSpawn()
        {
            base.OnSpawn();
            _clinic = GetComponent<Clinic>();
            if (_clinic == null)
            {
                Debug.LogWarning("[ClinicStateSyncer] No Clinic component found");
                return;
            }
            _smi = _clinic.GetSMI<Clinic.ClinicSM.Instance>();
        }

        protected override int SampleCurrentStateId()
        {
            if (_smi == null || _smi.sm == null)
                return -1;

            var sm = _smi.sm;
            if (_smi.IsInsideState(sm.operational.healing.newlyDoctored)) return 4;
            if (_smi.IsInsideState(sm.operational.healing.doctored)) return 3;
            if (_smi.IsInsideState(sm.operational.healing.undoctored)) return 2;
            if (_smi.IsInsideState(sm.operational.idle)) return 1;
            if (_smi.IsInsideState(sm.operational.healing)) return 2;
            if (_smi.IsInsideState(sm.unoperational)) return 0;
            return 0;
        }

        protected override void ApplyState(int stateId)
        {
            if (_smi == null || _smi.sm == null)
                return;

            var sm = _smi.sm;
            switch (stateId)
            {
                case 4:
                    if (!_smi.IsInsideState(sm.operational.healing.newlyDoctored))
                        _smi.TryGoTo(sm.operational.healing.newlyDoctored);
                    break;
                case 3:
                    if (!_smi.IsInsideState(sm.operational.healing.doctored))
                        _smi.TryGoTo(sm.operational.healing.doctored);
                    break;
                case 2:
                    if (!_smi.IsInsideState(sm.operational.healing.undoctored))
                        _smi.TryGoTo(sm.operational.healing.undoctored);
                    break;
                case 1:
                    if (!_smi.IsInsideState(sm.operational.idle))
                        _smi.TryGoTo(sm.operational.idle);
                    break;
                default:
                    if (!_smi.IsInsideState(sm.unoperational))
                        _smi.TryGoTo(sm.unoperational);
                    break;
            }
        }
    }
}
