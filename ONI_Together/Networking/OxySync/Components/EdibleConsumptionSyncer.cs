using KSerialization;
using Shared.OxySync;
using Shared.OxySync.Attributes;

namespace ONI_Together.Networking.OxySync.StateMachines
{
    [SkipSaveFileSerialization]
    public class EdibleConsumptionSyncer : NetworkBehaviour
    {
        private Edible _edible;

        [SyncVar(Hook = nameof(OnConsumptionChanged))]
        private bool _isBeingConsumed;

        [SyncVar(Epsilon = 0.01f, Hook = nameof(OnUnitsChanged))]
        private float _units;

        public override void OnSpawn()
        {
            base.OnSpawn();
            _edible = GetComponent<Edible>();
            if (_edible != null)
                _units = _edible.Units;
        }

        private void OnConsumptionChanged(bool _, bool value)
        {
            if (_edible != null)
                _edible.isBeingConsumed = value;
        }

        private void OnUnitsChanged(float _, float value)
        {
            if (_edible != null && value >= 0f)
                _edible.Units = value;
        }

        private void Update()
        {
            if (!isServer || !inSession || _edible == null)
                return;

            _isBeingConsumed = _edible.isBeingConsumed;
            _units = _edible.Units;
        }
    }
}
