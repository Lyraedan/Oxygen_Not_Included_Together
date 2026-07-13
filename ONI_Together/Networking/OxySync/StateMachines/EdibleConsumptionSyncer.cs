using KSerialization;
using Shared.OxySync.Attributes;

namespace ONI_Together.Networking.OxySync.StateMachines
{
    [SkipSaveFileSerialization]
    public class EdibleConsumptionSyncer : StateMachineSyncer
    {
        private Edible _edible;

        public override void OnSpawn()
        {
            base.OnSpawn();
            _edible = GetComponent<Edible>();
        }

        protected override int SampleCurrentStateId()
        {
            if (_edible == null)
                return -1;

            if (_edible.isBeingConsumed)
            {
                if (_edible.Units < 0.001f)
                    return 2;
                return 1;
            }
            return 0;
        }

        protected override void ApplyState(int stateId)
        {
            if (_edible == null)
                return;

            switch (stateId)
            {
                case 1:
                case 2:
                    _edible.isBeingConsumed = true;
                    break;
                default:
                    _edible.isBeingConsumed = false;
                    break;
            }
        }
    }
}
