namespace ONI_Together.Networking.OxySync.StateMachines
{
    [SkipSaveFileSerialization]
    public class GraveSyncer : StateMachineSyncer
    {
        private Grave.StatesInstance _smi;

        public override void OnSpawn()
        {
            base.OnSpawn();

            _smi = this.GetSMI<Grave.StatesInstance>();
        }

        protected override int SampleCurrentStateId()
        {
            if (_smi == null || _smi.sm == null)
                return -1;

            var sm = _smi.sm;
            if (_smi.IsInsideState(sm.full)) return 1;
            if (_smi.IsInsideState(sm.empty)) return 0;
            return 0;
        }

        protected override void ApplyState(int stateId)
        {
            if (_smi == null || _smi.sm == null)
                return;

            var sm = _smi.sm;
            switch (stateId)
            {
                case 1:
                    if (!_smi.IsInsideState(sm.full))
                        _smi.TryGoTo(sm.full);
                    break;
                default:
                    if (!_smi.IsInsideState(sm.empty))
                        _smi.TryGoTo(sm.empty);
                    break;
            }
        }
    }
}
