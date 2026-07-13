using KSerialization;
using Shared.OxySync;
using Shared.OxySync.Attributes;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    public class GenericGeneratorSyncComponent : NetworkBehaviour
    {
        private Generator _generator;

        [SyncVar(Hook = nameof(OnJoulesChanged))]
        private float _joulesAvailable;

        public override void OnSpawn()
        {
            base.OnSpawn();
            _generator = GetComponent<Generator>();
        }

        private void Update()
        {
            if (!isServer || _generator == null) return;
            _joulesAvailable = _generator.JoulesAvailable;
        }

        private void OnJoulesChanged(float oldValue, float newValue)
        {
            if (_generator == null) return;
            _generator.AssignJoulesAvailable(newValue);
        }
    }
}
