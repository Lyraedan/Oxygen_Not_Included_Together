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

        [SyncVar(Hook = nameof(OnOldAgeChanged))]
        private float _oldAge;

        [SyncVar(Hook = nameof(OnWiltingChanged), SendMode = (int) PacketSendMode.ReliableImmediate)]
        private bool _isWilting;

        [SyncVar(Hook = nameof(OnHarvestReadyChanged), SendMode = (int) PacketSendMode.ReliableImmediate)]
        private bool _isHarvestReady;

        [SyncVar(Hook = nameof(OnHarvestWhenReadyChanged), SendMode = (int)PacketSendMode.ReliableImmediate)]
        private bool _harvestWhenReady;

        [SyncVar(Hook = nameof(OnMarkedForHarvestChanged), SendMode = (int)PacketSendMode.ReliableImmediate)]
        private bool _markedForHarvest;

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
			_oldAge = _growing.PercentOldAge();
            _isWilting = _wilt != null && _wilt.IsWilting();
            _isHarvestReady = _harvest != null && _harvest.CanBeHarvested();
			_harvestWhenReady = _harvest != null && _harvest.HarvestWhenReady;
			_markedForHarvest = _harvest != null && _harvest.MarkedForHarvest;
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

		private void OnOldAgeChanged(float oldValue, float newValue)
		{
			if (_growing?.oldAge == null) return;
			_growing.oldAge.SetValue(_growing.oldAge.GetMax() * Mathf.Clamp01(newValue));
		}

        private void OnHarvestReadyChanged(bool oldValue, bool newValue)
        {
			RefreshAnimation();
        }

		private void OnHarvestWhenReadyChanged(bool oldValue, bool newValue)
		{
			if (_harvest == null || _harvest.HarvestWhenReady == newValue) return;
			_harvest.SetHarvestWhenReady(newValue);
		}

		private void OnMarkedForHarvestChanged(bool oldValue, bool newValue)
		{
			if (_harvest == null || _harvest.MarkedForHarvest == newValue) return;
			_harvest.MarkedForHarvest = newValue;
			RefreshAnimation();
		}

		private void RefreshAnimation()
		{
			if (TryGetComponent<KBatchedAnimController>(out var kbac))
			{
				kbac.SetVisiblity(true);
				kbac.forceRebuild = true;
			}
		}
    }
}
