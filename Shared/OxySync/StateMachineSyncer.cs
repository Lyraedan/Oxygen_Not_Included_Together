using KSerialization;
using ONI_Together.Networking;
using Shared.OxySync;
using Shared.OxySync.Attributes;

namespace Shared.OxySync
{
    [SkipSaveFileSerialization]
    public abstract class StateMachineSyncer : NetworkBehaviour
    {
        [SyncVar(SendMode = (int) PacketSendMode.ReliableImmediate)]
        private int _currentStateId;

        protected int _lastAppliedStateId = -1;

        protected bool RunLocallyOnClients = false;

        protected abstract int SampleCurrentStateId();

        protected abstract void ApplyState(int stateId);

        protected abstract StateMachine.Instance GetStateMachineInstance();

        protected virtual void OnServerSampleExtra() { }

        protected virtual void OnClientApplyExtra() { }

        protected void RegisterFrozenInstance()
        {
            if (isClient && !RunLocallyOnClients)
            {
                var smi = GetStateMachineInstance();
                if (smi != null)
                    FrozenStateMachineTracker.Freeze(smi);
            }
        }

        protected void UnregisterFrozenInstance()
        {
            if (isClient && !RunLocallyOnClients)
            {
                var smi = GetStateMachineInstance();
                if (smi != null)
                    FrozenStateMachineTracker.Unfreeze(smi);
            }
        }

        protected void Start()
        {
            base.Start();
            StartCoroutine(FreezeWhenReady());
        }

        public override void OnCleanUp()
        {
            UnregisterFrozenInstance();
            base.OnCleanUp();
        }

        private System.Collections.IEnumerator FreezeWhenReady()
        {
            while (isClient && !RunLocallyOnClients && GetStateMachineInstance() == null)
                yield return null;

            RegisterFrozenInstance();
        }

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
                _currentStateId = id;
            OnServerSampleExtra();
        }
    }
}
