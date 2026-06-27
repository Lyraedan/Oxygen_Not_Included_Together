using KSerialization;
using ONI_Together.Misc;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    public class ToiletStructureSyncer : NetworkBehaviour
    {
        private FlushToilet _flushToilet;
        private Toilet _outhouseToilet;
        private Storage _storage;
        private ConduitConsumer _conduitConsumer;
        private KPrefabID _prefabID;
        private bool _isFlushToilet;
        private bool _storageDirty;
        private float _syncTimer;
        private const float STORAGE_SYNC_DELAY = 0.2f;

        [SyncVar(Hook = nameof(OnStorageChanged))]
        private byte[] _storageBlob;

        [SyncVar]
        private int _flushesUsed;

        [SyncVar]
        private float _waterPct;

        [SyncVar]
        private float _wastePct;

        [SyncVar]
        private float _gunkPct;

        public override void OnSpawn()
        {
            base.OnSpawn();
            _flushToilet = GetComponent<FlushToilet>();
            _outhouseToilet = GetComponent<Toilet>();
            _storage = GetComponent<Storage>();
            _conduitConsumer = GetComponent<ConduitConsumer>();
            _prefabID = GetComponent<KPrefabID>();
            _isFlushToilet = _flushToilet != null;

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

        private void Update()
        {
            if (isClient)
            {
                if (_isFlushToilet && _flushToilet != null)
                {
                    _flushToilet.fillMeter?.SetPositionPercent(_waterPct);
                    _flushToilet.contaminationMeter?.SetPositionPercent(_wastePct);
                    _flushToilet.gunkMeter?.SetPositionPercent(_gunkPct);
                }
                else if (_outhouseToilet != null)
                {
                    _outhouseToilet.meter?.SetPositionPercent(
                        Mathf.Clamp01((float)_flushesUsed / _outhouseToilet.maxFlushes));
                }
                return;
            }

            if (!isServer || !inSession) return;

            if (_isFlushToilet)
            {
                if (!_storageDirty || _storage == null) return;
                _syncTimer += Time.unscaledDeltaTime;
                if (_syncTimer < STORAGE_SYNC_DELAY) return;
                _syncTimer = 0f;
                _storageDirty = false;

                _storageBlob = BuildingUtils.EncodeStorageToBytes(_storage);

                float totalWater = 0f, totalWaste = 0f, totalGunk = 0f;
                foreach (var item in _storage.items)
                {
                    if (item == null) continue;
                    var pe = item.GetComponent<PrimaryElement>();
                    if (pe == null) continue;
                    if (pe.ElementID == SimHashes.Water) totalWater += pe.Mass;
                    else if (pe.ElementID == SimHashes.DirtyWater) totalWaste += pe.Mass;
                    else if (pe.ElementID == GunkMonitor.GunkElement) totalGunk += pe.Mass;
                }

                _waterPct = Mathf.Clamp01(totalWater / _flushToilet.massConsumedPerUse);
                _wastePct = Mathf.Clamp01(totalWaste / _flushToilet.massEmittedPerUse);
                _gunkPct = Mathf.Clamp01(totalGunk / _flushToilet.massEmittedPerUse);

                bool full = totalWater >= _flushToilet.massConsumedPerUse;
                if (_conduitConsumer != null)
                    _conduitConsumer.enabled = !full;
            }
            else
            {
                _flushesUsed = _outhouseToilet != null ? _outhouseToilet.FlushesUsed : 0;
            }
        }

        private void OnStorageChanged(byte[] oldValue, byte[] newValue)
        {
            if (!_isFlushToilet || _storage == null || newValue == null) return;

            BuildingUtils.RebuildStorageFromBytes(_storage, newValue);

            float totalWater = 0f;
            foreach (var item in _storage.items)
            {
                if (item == null) continue;
                var pe = item.GetComponent<PrimaryElement>();
                if (pe == null) continue;
                if (pe.ElementID == SimHashes.Water) totalWater += pe.Mass;
            }

            if (_flushToilet == null) return;

            bool full = totalWater >= _flushToilet.massConsumedPerUse;
            if (_conduitConsumer != null)
                _conduitConsumer.enabled = !full;
        }
    }
}
