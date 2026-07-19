using ONI_Together.Misc;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.StateMachines
{
    [SkipSaveFileSerialization]
    public class AlgaeHabitatSyncer : StateMachineSyncer
    {
        private AlgaeHabitat _algaeHabitat;
        private AlgaeHabitat.SMInstance _smi;
        private Storage _storage;
        private Storage _pollutedWaterStorage;

        private bool _storageDirty;
        private float _storageSyncTimer;

        private bool _pollutedWaterStorageDirty;
        private float _pollutedWaterStorageSyncTimer;

        private const float STORAGE_SYNC_DELAY = 0.2f;

        [SyncVar(SendMode = (int)PacketSendMode.ReliableImmediate)]
        private byte[] _storageBlob;

        private byte[] _lastAppliedStorageBlob;

        [SyncVar(SendMode = (int)PacketSendMode.ReliableImmediate)]
        private byte[] _pollutedWaterStorageBlob;

        private byte[] _lastAppliedPollutedWaterStorageBlob;

        [SyncVar(SendMode = (int)PacketSendMode.ReliableImmediate)]
        private float _lightBonusMultiplier;

        public override void OnSpawn()
        {
            base.OnSpawn();

            _algaeHabitat = GetComponent<AlgaeHabitat>();
            _smi = this.GetSMI<AlgaeHabitat.SMInstance>();
            _storage = GetComponent<Storage>();
            _pollutedWaterStorage = _algaeHabitat.pollutedWaterStorage;

            if (_storage != null)
            {
                _storage.OnStorageChange += OnStorageChanged;
                _storage.Subscribe((int)GameHashes.OnStorageChange, OnStorageChangedGameHash);
            }

            if (_pollutedWaterStorage != null)
            {
                _pollutedWaterStorage.OnStorageChange += OnPollutedWaterStorageChanged;
                _pollutedWaterStorage.Subscribe((int)GameHashes.OnStorageChange, OnPollutedWaterStorageChangedGameHash);
            }
        }

        public override void OnCleanUp()
        {
            if (_storage != null)
            {
                _storage.OnStorageChange -= OnStorageChanged;
                _storage.Unsubscribe((int)GameHashes.OnStorageChange, OnStorageChangedGameHash);
            }

            if (_pollutedWaterStorage != null)
            {
                _pollutedWaterStorage.OnStorageChange -= OnPollutedWaterStorageChanged;
                _pollutedWaterStorage.Unsubscribe((int)GameHashes.OnStorageChange, OnPollutedWaterStorageChangedGameHash);
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

        private void OnPollutedWaterStorageChanged(GameObject _)
        {
            _pollutedWaterStorageDirty = true;
        }

        private void OnPollutedWaterStorageChangedGameHash(object _)
        {
            _pollutedWaterStorageDirty = true;
        }

        protected override int SampleCurrentStateId()
        {
            if (_smi == null || _smi.sm == null)
                return -1;

            var sm = _smi.sm;
            if (_smi.IsInsideState(sm.stoppedGeneratingOxygenTransition)) return 10;
            if (_smi.IsInsideState(sm.stoppedGeneratingOxygen)) return 9;
            if (_smi.IsInsideState(sm.generatingOxygen)) return 8;
            if (_smi.IsInsideState(sm.lostAlgae)) return 7;
            if (_smi.IsInsideState(sm.gotEmptied)) return 6;
            if (_smi.IsInsideState(sm.needsEmptying)) return 5;
            if (_smi.IsInsideState(sm.gotWater)) return 4;
            if (_smi.IsInsideState(sm.noWater)) return 3;
            if (_smi.IsInsideState(sm.gotAlgae)) return 2;
            if (_smi.IsInsideState(sm.notoperational)) return 1;
            if (_smi.IsInsideState(sm.noAlgae)) return 0;
            return 0;
        }

        protected override void ApplyState(int stateId)
        {
            if (_smi == null || _smi.sm == null)
                return;

            var sm = _smi.sm;
            switch (stateId)
            {
                case 10:
                    if (!_smi.IsInsideState(sm.stoppedGeneratingOxygenTransition))
                        _smi.TryGoTo(sm.stoppedGeneratingOxygenTransition);
                    break;
                case 9:
                    if (!_smi.IsInsideState(sm.stoppedGeneratingOxygen))
                        _smi.TryGoTo(sm.stoppedGeneratingOxygen);
                    break;
                case 8:
                    if (!_smi.IsInsideState(sm.generatingOxygen))
                        _smi.TryGoTo(sm.generatingOxygen);
                    break;
                case 7:
                    if (!_smi.IsInsideState(sm.lostAlgae))
                        _smi.TryGoTo(sm.lostAlgae);
                    break;
                case 6:
                    if (!_smi.IsInsideState(sm.gotEmptied))
                        _smi.TryGoTo(sm.gotEmptied);
                    break;
                case 5:
                    if (!_smi.IsInsideState(sm.needsEmptying))
                        _smi.TryGoTo(sm.needsEmptying);
                    break;
                case 4:
                    if (!_smi.IsInsideState(sm.gotWater))
                        _smi.TryGoTo(sm.gotWater);
                    break;
                case 3:
                    if (!_smi.IsInsideState(sm.noWater))
                        _smi.TryGoTo(sm.noWater);
                    break;
                case 2:
                    if (!_smi.IsInsideState(sm.gotAlgae))
                        _smi.TryGoTo(sm.gotAlgae);
                    break;
                case 1:
                    if (!_smi.IsInsideState(sm.notoperational))
                        _smi.TryGoTo(sm.notoperational);
                    break;
                default:
                    if (!_smi.IsInsideState(sm.noAlgae))
                        _smi.TryGoTo(sm.noAlgae);
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

            if (_pollutedWaterStorageDirty && _pollutedWaterStorage != null)
            {
                _pollutedWaterStorageSyncTimer += Time.unscaledDeltaTime;
                if (_pollutedWaterStorageSyncTimer >= STORAGE_SYNC_DELAY)
                {
                    _pollutedWaterStorageSyncTimer = 0f;
                    _pollutedWaterStorageDirty = false;
                    _pollutedWaterStorageBlob = BuildingUtils.EncodeStorageToBytes(_pollutedWaterStorage);
                }
            }

            _lightBonusMultiplier = _algaeHabitat.lightBonusMultiplier;
        }

        protected override void OnClientApplyExtra()
        {
            if (_storage != null && _storageBlob != null && _storageBlob != _lastAppliedStorageBlob)
            {
                BuildingUtils.RebuildStorageFromBytes(_storage, _storageBlob);
                _lastAppliedStorageBlob = _storageBlob;
            }

            if (_pollutedWaterStorage != null && _pollutedWaterStorageBlob != null && _pollutedWaterStorageBlob != _lastAppliedPollutedWaterStorageBlob)
            {
                BuildingUtils.RebuildStorageFromBytes(_pollutedWaterStorage, _pollutedWaterStorageBlob);
                _lastAppliedPollutedWaterStorageBlob = _pollutedWaterStorageBlob;
            }

            _algaeHabitat.lightBonusMultiplier = _lightBonusMultiplier;
        }
    }
}
