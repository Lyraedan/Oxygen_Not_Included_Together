using ONI_Together.Misc;
using ONI_Together.Networking.OxySync.StateMachines;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    public class BottleEmptierSyncer : StateMachineSyncer
    {
        private BottleEmptier _bottleEmptier;
        private BottleEmptier.StatesInstance _smi;
        private Storage _storage;

        private bool _storageDirty;
        private float _storageSyncTimer;

        private const float STORAGE_SYNC_DELAY = 0.2f;

        [SyncVar(SendMode = (int)PacketSendMode.ReliableImmediate)]
        private byte[] _storageBlob;

        private byte[] _lastAppliedStorageBlob;

        public override void OnSpawn()
        {
            base.OnSpawn();

            _bottleEmptier = GetComponent<BottleEmptier>();
            _smi = _bottleEmptier.smi;
            _storage = GetComponent<Storage>();

            if (_storage != null)
                _storage.OnStorageChange += OnLocalStorageChanged;
        }

        public override void OnCleanUp()
        {
            if (_storage != null)
                _storage.OnStorageChange -= OnLocalStorageChanged;

            base.OnCleanUp();
        }

        private void OnLocalStorageChanged(GameObject _)
        {
            _storageDirty = true;
        }

        protected override int SampleCurrentStateId()
        {
            if (_smi == null || _smi.sm == null)
                return -1;

            var sm = _smi.sm;
            if (_smi.IsInsideState(sm.emptying)) return 2;
            if (_smi.IsInsideState(sm.unoperational)) return 1;
            if (_smi.IsInsideState(sm.waitingfordelivery)) return 0;
            return 0;
        }

        protected override void ApplyState(int stateId)
        {
            if (_smi == null || _smi.sm == null)
                return;

            var sm = _smi.sm;

            switch (stateId)
            {
                case 2:
                    if (!_smi.IsInsideState(sm.emptying))
                        _smi.TryGoTo(sm.emptying);
                    break;
                case 1:
                    if (!_smi.IsInsideState(sm.unoperational))
                        _smi.TryGoTo(sm.unoperational);
                    break;
                default:
                    if (!_smi.IsInsideState(sm.waitingfordelivery))
                        _smi.TryGoTo(sm.waitingfordelivery);
                    break;
            }
        }

        protected override void OnServerSampleExtra()
        {
            if (_storageDirty)
            {
                _storageSyncTimer += Time.unscaledDeltaTime;
                if (_storageSyncTimer >= STORAGE_SYNC_DELAY)
                {
                    _storageSyncTimer = 0f;
                    _storageBlob = BuildingUtils.EncodeStorageToBytes(_storage);
                    _storageDirty = false;
                }
            }
        }

        protected override void OnClientApplyExtra()
        {
            if (_storageBlob != null && _storageBlob != _lastAppliedStorageBlob)
            {
                BuildingUtils.RebuildStorageFromBytes(_storage, _storageBlob);
                _lastAppliedStorageBlob = _storageBlob;
            }
        }
    }
}
