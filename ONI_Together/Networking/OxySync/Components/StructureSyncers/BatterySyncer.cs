using KSerialization;
using ONI_Together.DebugTools;
using ONI_Together.Patches.World;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    public class BatterySyncer : NetworkBehaviour
    {
        private Battery _battery;

        [SyncVar(Hook = nameof(OnJoulesChanged))]
        private float _joulesAvailable;

        public override void OnSpawn()
        {
            base.OnSpawn();
            _battery = GetComponent<Battery>();
        }

        private void Update()
        {
            if (_battery == null) return;
            if (isClient)
            {
                UpdateMeter(_joulesAvailable);
                return;
            }

            if (!isServer) return;

            _joulesAvailable = _battery.JoulesAvailable;
        }

        private void OnJoulesChanged(float oldValue, float newValue)
        {
            if (_battery == null) return;

            _battery.joulesAvailable = newValue;
            RefreshBatteryTracker();
            UpdateMeter(_joulesAvailable);
        }

        private void UpdateMeter(float joules)
        {
            try
            {
                var meter = _battery?.meter;
                if (meter == null) return;

                if (_battery.capacity <= 0f) return;

                float percent = Mathf.Clamp01(joules / _battery.capacity);
                meter.SetPositionPercent(percent);
            }
            catch (System.Exception ex)
            {
                DebugConsole.LogError($"[BatterySyncComponent] Meter update failed: {ex}");
            }
        }

        private void RefreshBatteryTracker()
        {
            var tracker = TrackerTool.Instance?.GetWorldTracker<BatteryTracker>(gameObject.GetMyWorldId());
            if (tracker == null) return;

            using var allowClientRefresh = BatteryTrackerPatch.AllowClientRefresh();
            tracker.UpdateData();
        }
    }
}
