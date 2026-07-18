namespace ONI_Together.Networking.OxySync.StateMachines
{
    [SkipSaveFileSerialization]
    public class FoodSmokerSyncer : StateMachineSyncer
    {
        private FoodSmoker.StatesInstance _smi;

        public override void OnSpawn()
        {
            base.OnSpawn();

            _smi = this.GetSMI<FoodSmoker.StatesInstance>();
        }

        protected override int SampleCurrentStateId()
        {
            if (_smi == null || _smi.sm == null)
                return -1;

            var sm = _smi.sm;
            if (_smi.IsInsideState(sm.requestEmpty)) return 1;
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
                    if (!_smi.IsInsideState(sm.requestEmpty))
                        _smi.TryGoTo(sm.requestEmpty);
                    break;
                default:
                    if (!_smi.IsInsideState(sm.working))
                        _smi.TryGoTo(sm.working);
                    break;
            }
        }
    }
}
