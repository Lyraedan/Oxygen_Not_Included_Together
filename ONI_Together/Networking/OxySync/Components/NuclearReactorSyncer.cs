using KSerialization;
using ONI_Together.Misc;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    public class NuclearReactorSyncer : NetworkBehaviour
    {
        private Reactor _reactor;
        private Reactor.StatesInstance _smi;
        private Storage _supplyStorage;
        private Storage _reactionStorage;
        private Storage _wasteStorage;

        private bool _supplyDirty;
        private bool _reactionDirty;
        private bool _wasteDirty;
        private float _storageSyncTimer;
        private int _lastAppliedMajorState = -1;

        private const float STORAGE_SYNC_DELAY = 0.2f;

        [SyncVar]
        private float _fuelTemp;

        [SyncVar]
        private int _majorState;

        [SyncVar]
        private bool _reactionUnderway;

        [SyncVar]
        private bool _meltingDown;

        [SyncVar]
        private bool _melted;

        [SyncVar]
        private bool _canVent;

        [SyncVar]
        private float _meltdownMassRemaining;

        [SyncVar]
        private float _timeSinceMeltdown;

        [SyncVar]
        private float _spentFuel;

        [SyncVar]
        private int _numCyclesRunning;

        [SyncVar]
        private bool _fuelDeliveryEnabled;

        [SyncVar]
        private float _timeSinceMeltdownEmit;

        [SyncVar]
        private float _emitRads;

        [SyncVar]
        private float _waterMeterPercent;

        [SyncVar]
        private float _temperaturePercent;

        [SyncVar]
        private byte[] _supplyStorageBlob;

        [SyncVar]
        private byte[] _reactionStorageBlob;

        [SyncVar]
        private byte[] _wasteStorageBlob;

        public override void OnSpawn()
        {
            base.OnSpawn();
            _reactor = GetComponent<Reactor>();
            _smi = _reactor.smi;
            _supplyStorage = _reactor.supplyStorage;
            _reactionStorage = _reactor.reactionStorage;
            _wasteStorage = _reactor.wasteStorage;

            SubscribeStorageEvents();
        }

        public override void OnCleanUp()
        {
            UnsubscribeStorageEvents();
            base.OnCleanUp();
        }

        private void SubscribeStorageEvents()
        {
            if (_supplyStorage != null)
                _supplyStorage.OnStorageChange += OnSupplyStorageChanged;
            if (_reactionStorage != null)
                _reactionStorage.OnStorageChange += OnReactionStorageChanged;
            if (_wasteStorage != null)
                _wasteStorage.OnStorageChange += OnWasteStorageChanged;
        }

        private void UnsubscribeStorageEvents()
        {
            if (_supplyStorage != null)
                _supplyStorage.OnStorageChange -= OnSupplyStorageChanged;
            if (_reactionStorage != null)
                _reactionStorage.OnStorageChange -= OnReactionStorageChanged;
            if (_wasteStorage != null)
                _wasteStorage.OnStorageChange -= OnWasteStorageChanged;
        }

        private void OnSupplyStorageChanged(GameObject _) { _supplyDirty = true; }
        private void OnReactionStorageChanged(GameObject _) { _reactionDirty = true; }
        private void OnWasteStorageChanged(GameObject _) { _wasteDirty = true; }

        private void Update()
        {
            if (isClient)
            {
                ApplyClientState();
                return;
            }

            if (!isServer || !inSession) return;

            var sm = _smi.sm;

            _fuelTemp = Mathf.Max(0f, _reactor.FuelTemperature);

            _majorState = _smi.IsInsideState(sm.dead) ? 3
                : _smi.IsInsideState(sm.meltdown) ? 2
                : _smi.IsInsideState(sm.on) ? 1 : 0;

            _reactionUnderway = sm.reactionUnderway.Get(_smi);
            _meltingDown = sm.meltingDown.Get(_smi);
            _melted = sm.melted.Get(_smi);
            _canVent = sm.canVent.Get(_smi);
            _meltdownMassRemaining = sm.meltdownMassRemaining.Get(_smi);
            _timeSinceMeltdown = sm.timeSinceMeltdown.Get(_smi);

            _spentFuel = _reactor.spentFuel;
            _numCyclesRunning = _reactor.numCyclesRunning;
            _fuelDeliveryEnabled = _reactor.fuelDeliveryEnabled;
            _timeSinceMeltdownEmit = _reactor.timeSinceMeltdownEmit;
            _emitRads = _reactor.radEmitter.emitRads;

            PrimaryElement coolant = _reactor.GetStoredCoolant();
            _waterMeterPercent = coolant ? coolant.Mass / 90f : 0f;

            PrimaryElement activeFuel = _reactor.GetActiveFuel();
            _temperaturePercent = activeFuel != null
                ? Mathf.Clamp01(activeFuel.Temperature / 3000f) / Reactor.meterFrameScaleHack
                : 0f;

            if (_supplyDirty || _reactionDirty || _wasteDirty)
            {
                _storageSyncTimer += Time.unscaledDeltaTime;
                if (_storageSyncTimer >= STORAGE_SYNC_DELAY)
                {
                    _storageSyncTimer = 0f;
                    if (_supplyDirty) { _supplyStorageBlob = BuildingUtils.EncodeStorageToBytes(_supplyStorage); _supplyDirty = false; }
                    if (_reactionDirty) { _reactionStorageBlob = BuildingUtils.EncodeStorageToBytes(_reactionStorage); _reactionDirty = false; }
                    if (_wasteDirty) { _wasteStorageBlob = BuildingUtils.EncodeStorageToBytes(_wasteStorage); _wasteDirty = false; }
                }
            }
        }

        private void ApplyClientState()
        {
            if (_reactor == null || _smi == null) return;

            var sm = _smi.sm;

            if (_supplyStorageBlob != null)
                BuildingUtils.RebuildStorageFromBytes(_supplyStorage, _supplyStorageBlob);
            if (_reactionStorageBlob != null)
                BuildingUtils.RebuildStorageFromBytes(_reactionStorage, _reactionStorageBlob);
            if (_wasteStorageBlob != null)
                BuildingUtils.RebuildStorageFromBytes(_wasteStorage, _wasteStorageBlob);

            if (_reactionStorageBlob != null)
            {
                var fuel = _reactionStorage.FindFirst(SimHashes.EnrichedUranium.CreateTag());
                if (fuel != null)
                {
                    var pe = fuel.GetComponent<PrimaryElement>();
                    if (pe != null) pe.Temperature = _fuelTemp;
                }
            }

            sm.reactionUnderway.Set(_reactionUnderway, _smi);
            sm.meltingDown.Set(_meltingDown, _smi);
            sm.melted.Set(_melted, _smi);
            sm.meltdownMassRemaining.Set(_meltdownMassRemaining, _smi);
            sm.timeSinceMeltdown.Set(_timeSinceMeltdown, _smi);
            sm.canVent.Set(_canVent, _smi);

            _reactor.spentFuel = _spentFuel;
            _reactor.numCyclesRunning = _numCyclesRunning;
            _reactor.fuelDeliveryEnabled = _fuelDeliveryEnabled;
            _reactor.timeSinceMeltdownEmit = _timeSinceMeltdownEmit;
            _reactor.radEmitter.emitRads = _emitRads;
            _reactor.waterMeter.SetPositionPercent(_waterMeterPercent);
            _reactor.temperatureMeter.SetPositionPercent(_temperaturePercent);

            if (_lastAppliedMajorState != _majorState)
            {
                _lastAppliedMajorState = _majorState;
                ForceStateTransition(_majorState);
            }
        }

        private void ForceStateTransition(int targetState)
        {
            var sm = _smi.sm;

            switch (targetState)
            {
                case 0:
                    if (_smi.IsInsideState(sm.on))
                        _smi.GoTo(sm.off);
                    break;

                case 1:
                    if (_smi.IsInsideState(sm.off))
                        sm.reactionUnderway.Set(true, _smi);
                    break;

                case 2:
                    if (!_smi.IsInsideState(sm.meltdown) && !_smi.IsInsideState(sm.dead))
                    {
                        _supplyStorage?.ConsumeAllIgnoringDisease();
                        _reactionStorage?.ConsumeAllIgnoringDisease();
                        _wasteStorage?.ConsumeAllIgnoringDisease();
                        float totalMass = (_supplyStorage?.MassStored() ?? 0f)
                            + (_reactionStorage?.MassStored() ?? 0f)
                            + (_wasteStorage?.MassStored() ?? 0f);
                        sm.meltdownMassRemaining.Set(10f + totalMass + _reactor.spentFuel, _smi);
                        _smi.GoTo(sm.meltdown.pre);
                    }
                    break;

                case 3:
                    if (_smi.IsInsideState(sm.meltdown))
                        sm.meltdownMassRemaining.Set(0f, _smi);
                    else if (!_smi.IsInsideState(sm.dead))
                        _smi.GoTo(sm.dead);
                    break;
            }
        }
    }
}
