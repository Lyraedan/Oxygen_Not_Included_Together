using ONI_Together.Misc;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.StateMachines
{
    /// <summary>
    /// Handles syncing for the bulk of Oxygen Diffuser's and Sublimator's function. (some of it is handled by the syncer for ElementConverters)
    /// </summary>
    [SkipSaveFileSerialization]
    public class ElectrolyzerStateSyncer : StateMachineSyncer
    {
        private Electrolyzer _electrolyzer;
        private Electrolyzer.StatesInstance _smi;
        private Storage _storage;

        private bool _storageDirty;
        private float _storageSyncTimer;

        private const float STORAGE_SYNC_DELAY = 0.2f;

        [SyncVar(SendMode = (int)PacketSendMode.ReliableImmediate)]
        private byte[] _storageBlob;

        private byte[] _lastAppliedStorageBlob;

        [SyncVar(SendMode = (int)PacketSendMode.ReliableImmediate)]
        private float _maxMass;

        public override void OnSpawn()
        {
            base.OnSpawn();

            _electrolyzer = GetComponent<Electrolyzer>();
            _smi = this.GetSMI<Electrolyzer.StatesInstance>();
            _storage = GetComponent<Storage>();

            if (_storage != null)
            {
                _storage.OnStorageChange += OnStorageChanged;
                _storage.Subscribe((int)GameHashes.OnStorageChange, OnStorageChangedGameHash);
                _storageDirty = true;
            }
        }

        public override void OnCleanUp()
        {
            if (_storage != null)
            {
                _storage.OnStorageChange -= OnStorageChanged;
                _storage.Unsubscribe((int)GameHashes.OnStorageChange, OnStorageChangedGameHash);
            }

            base.OnCleanUp();
        }

        private void OnStorageChanged(GameObject _)
        {
            _storageDirty = true;
        }

        private void OnStorageChangedGameHash(object _)
        {
            _storageDirty = true;
        }

        protected override int SampleCurrentStateId()
        {
            if (_smi == null || _smi.sm == null)
                return -1;

            var sm = _smi.sm;
            if (_smi.IsInsideState(sm.waiting)) return 3;
            if (_smi.IsInsideState(sm.converting)) return 2;
            if (_smi.IsInsideState(sm.overpressure)) return 1;
            if (_smi.IsInsideState(sm.disabled)) return 0;
            return 0;
        }

        protected override void ApplyState(int stateId)
        {
            if (_smi == null || _smi.sm == null)
                return;

            var sm = _smi.sm;
            switch (stateId)
            {
                case 3:
                    if (!_smi.IsInsideState(sm.waiting))
                        _smi.TryGoTo(sm.waiting);
                    break;
                case 2:
                    if (!_smi.IsInsideState(sm.converting))
                        _smi.TryGoTo(sm.converting);
                    break;
                case 1:
                    if (!_smi.IsInsideState(sm.overpressure))
                        _smi.TryGoTo(sm.overpressure);
                    break;
                default:
                    if (!_smi.IsInsideState(sm.disabled))
                        _smi.TryGoTo(sm.disabled);
                    break;
            }
        }

        protected override void OnServerSampleExtra()
        {
            if (_storageDirty && _storage != null)
            {
                _storageSyncTimer += Time.unscaledDeltaTime;
                if (_storageSyncTimer >= STORAGE_SYNC_DELAY)
                {
                    _storageSyncTimer = 0f;
                    _storageDirty = false;
                    _storageBlob = BuildingUtils.EncodeStorageToBytes(_storage);
                }
            }

            _maxMass = _electrolyzer.maxMass;
        }

        protected override void OnClientApplyExtra()
        {
            if (_storage != null && _storageBlob != null && _storageBlob != _lastAppliedStorageBlob)
            {
                BuildingUtils.RebuildStorageFromBytes(_storage, _storageBlob);
                _lastAppliedStorageBlob = _storageBlob;
                _electrolyzer.UpdateMeter();
            }

            _electrolyzer.maxMass = _maxMass;
        }
    }
}
