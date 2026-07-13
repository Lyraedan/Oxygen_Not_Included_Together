using KSerialization;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    public class EnergyGeneratorSyncComponent : NetworkBehaviour
    {
        private Generator _generator;
        private EnergyGenerator _energyGen;
        private Storage _storage;

        [SyncVar(Hook = nameof(OnJoulesChanged))]
        private float _joulesAvailable;

        [SyncVar(Hook = nameof(OnInputMassPercentChanged))]
        private float _inputMassPercent;

        public override void OnSpawn()
        {
            base.OnSpawn();
            _generator = GetComponent<Generator>();
            _energyGen = GetComponent<EnergyGenerator>();
            _storage = _energyGen?.storage;
        }

        private void Update()
        {
            if (_generator == null) return;
            
            if (isClient)
            {
                _energyGen.meter?.SetPositionPercent(_inputMassPercent); // Always updated the meter
                return;
            }
            
            if (!isServer) return;

            _joulesAvailable = _generator.JoulesAvailable;

            if (_energyGen != null && _energyGen.hasMeter && _storage != null)
            {
                var inputItem = _energyGen.formula.inputs[0];
                float availableMass = _storage.GetMassAvailable(inputItem.tag);
                _inputMassPercent = Mathf.Clamp01(availableMass / inputItem.maxStoredMass);
            }
        }

        private void OnJoulesChanged(float oldValue, float newValue)
        {
            if (_generator == null) return;
            _generator.AssignJoulesAvailable(newValue);
        }

        private void OnInputMassPercentChanged(float oldValue, float newValue)
        {
            if (_energyGen == null || !_energyGen.hasMeter) return;
            _energyGen.meter?.SetPositionPercent(newValue);
        }
    }
}
