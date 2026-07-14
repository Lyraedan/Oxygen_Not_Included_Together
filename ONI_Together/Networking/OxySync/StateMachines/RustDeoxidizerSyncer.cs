using KSerialization;
using Shared.OxySync.Attributes;

namespace ONI_Together.Networking.OxySync.StateMachines
{
    [SkipSaveFileSerialization]
    public class RustDeoxidizerSyncer : StateMachineSyncer
    {
        private RustDeoxidizer.StatesInstance _smi;

        public override void OnSpawn()
        {
            base.OnSpawn();

            _smi = this.GetSMI<RustDeoxidizer.StatesInstance>();
        }

        protected override int SampleCurrentStateId()
        {
            if (_smi == null || _smi.sm == null)
                return -1;

            var sm = _smi.sm;
            if (_smi.IsInsideState(sm.waiting)) return 3;
            if (_smi.IsInsideState(sm.converting)) return 2;
            if (_smi.IsInsideState(sm.overpressure)) return 1;
            if (_smi.IsInsideState(sm.disabled)) return 0;
            return 0;
        }

        protected override void ApplyState(int stateId)
        {
            if (_smi == null || _smi.sm == null)
                return;

            var sm = _smi.sm;
            switch (stateId)
            {
                case 3:
                    if (!_smi.IsInsideState(sm.waiting))
                        _smi.TryGoTo(sm.waiting);
                    break;
                case 2:
                    if (!_smi.IsInsideState(sm.converting))
                        _smi.TryGoTo(sm.converting);
                    break;
                case 1:
                    if (!_smi.IsInsideState(sm.overpressure))
                        _smi.TryGoTo(sm.overpressure);
                    break;
                default:
                    if (!_smi.IsInsideState(sm.disabled))
                        _smi.TryGoTo(sm.disabled);
                    break;
            }
        }
    }
}
