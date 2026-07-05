using KSerialization;
using Shared.OxySync.Attributes;

namespace ONI_Together.Networking.OxySync.StateMachines
{
    [SkipSaveFileSerialization]
    public class RottableStateSyncer : StateMachineSyncer
    {
        private Rottable.Instance _smi;

        public override void OnSpawn()
        {
            base.OnSpawn();
            _smi = this.GetSMI<Rottable.Instance>();
        }

        protected override int SampleCurrentStateId()
        {
            if (_smi == null || _smi.sm == null)
                return -1;

            var sm = _smi.sm;
            if (_smi.IsInsideState(sm.Spoiled)) return 3;
            if (_smi.IsInsideState(sm.Stale)) return 2;
            if (_smi.IsInsideState(sm.Stale_Pre)) return 2;
            if (_smi.IsInsideState(sm.Preserved)) return 1;
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
                    if (!_smi.IsInsideState(sm.Spoiled))
                        _smi.TryGoTo(sm.Spoiled);
                    break;
                case 2:
                    if (!_smi.IsInsideState(sm.Stale) && !_smi.IsInsideState(sm.Stale_Pre))
                        _smi.TryGoTo(sm.Stale);
                    break;
                case 1:
                    if (!_smi.IsInsideState(sm.Preserved))
                        _smi.TryGoTo(sm.Preserved);
                    break;
                default:
                    if (!_smi.IsInsideState(sm.Fresh))
                        _smi.TryGoTo(sm.Fresh);
                    break;
            }
        }
    }
}
