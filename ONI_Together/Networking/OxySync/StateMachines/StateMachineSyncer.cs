using KSerialization;
using Shared.OxySync;
using Shared.OxySync.Attributes;

namespace ONI_Together.Networking.OxySync.StateMachines
{
    [SkipSaveFileSerialization]
    public abstract class StateMachineSyncer : NetworkBehaviour
    {
        [SyncVar(SendMode = (int) PacketSendMode.ReliableImmediate)]
        private int _currentStateId;

        protected int _lastAppliedStateId = -1;

        protected abstract int SampleCurrentStateId();

        protected abstract void ApplyState(int stateId);

        protected virtual void OnServerSampleExtra() { }

        protected virtual void OnClientApplyExtra() { }

        private void Update()
        {
            if (isClient)
            {
                if (_lastAppliedStateId != _currentStateId)
                {
                    _lastAppliedStateId = _currentStateId;
                    ApplyState(_currentStateId);
                }
                OnClientApplyExtra();
                return;
            }

            if (!isServer || !inSession)
                return;

            int id = SampleCurrentStateId();
            if (id != _currentStateId)
            {
                _currentStateId = id;
            }
            OnServerSampleExtra();
        }
    }
}
