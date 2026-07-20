using Shared.OxySync;
using Shared.OxySync.Attributes;

namespace ONI_Together.Networking.OxySync.StateMachines
{
    [SkipSaveFileSerialization]
    public class GraveStateSyncer : StateMachineSyncer
    {
        private Grave _grave;
        private Grave.StatesInstance _smi;

        [SyncVar(SendMode = (int)PacketSendMode.ReliableImmediate)]
        private string _graveName;

        [SyncVar(SendMode = (int)PacketSendMode.ReliableImmediate)]
        private string _graveAnim;

        [SyncVar]
        private int _epitaphIdx;

        [SyncVar]
        private float _burialTime;

        public override void OnSpawn()
        {
            base.OnSpawn();

            _grave = GetComponent<Grave>();
            _smi = this.GetSMI<Grave.StatesInstance>();
        }

        protected override void OnServerSampleExtra()
        {
            if (_grave == null)
                return;

            _graveName = _grave.graveName;
            _graveAnim = _grave.graveAnim;
            _epitaphIdx = _grave.epitaphIdx;
            _burialTime = _grave.burialTime;
        }

        protected override void OnClientApplyExtra()
        {
            if (_grave == null)
                return;

            _grave.graveName = _graveName;
            _grave.graveAnim = _graveAnim;
            _grave.epitaphIdx = _epitaphIdx;
            _grave.burialTime = _burialTime;
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
