using KSerialization;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    public class PlantSyncer : NetworkBehaviour
    {
        private Growing _growing;
        private WiltCondition _wilt;
        private HarvestDesignatable _harvest;

        [SyncVar(Hook = nameof(OnMaturityChanged))]
        private float _maturity;

        [SyncVar(Hook = nameof(OnWiltingChanged), SendMode = (int) PacketSendMode.ReliableImmediate)]
        private bool _isWilting;

        [SyncVar(Hook = nameof(OnHarvestReadyChanged), SendMode = (int) PacketSendMode.ReliableImmediate)]
        private bool _isHarvestReady;

        public override void OnSpawn()
        {
            base.OnSpawn();
            SyncInterval = 2f;
            _growing = GetComponent<Growing>();
            _wilt = GetComponent<WiltCondition>();
            _harvest = GetComponent<HarvestDesignatable>();
        }

        private void Update()
        {
            if (_growing == null) return;
            if (isClient) return;
            if (!isServer || !inSession) return;

            _maturity = _growing.PercentGrown();
            _isWilting = _wilt != null && _wilt.IsWilting();
            _isHarvestReady = _harvest != null && _harvest.CanBeHarvested();
        }

        private void OnMaturityChanged(float oldValue, float newValue)
        {
            if (_growing == null) return;

            _growing.OverrideMaturityLevel(newValue);

            if (TryGetComponent<KBatchedAnimController>(out var kbac))
            {
                kbac.SetVisiblity(true);
                kbac.forceRebuild = true;
            }
        }

        private void OnWiltingChanged(bool oldValue, bool newValue)
        {
            if (_wilt == null) return;

            if (newValue)
                _wilt.DoWilt();
            else
                _wilt.DoRecover();
        }

        private void OnHarvestReadyChanged(bool oldValue, bool newValue)
        {
        }
    }
}
