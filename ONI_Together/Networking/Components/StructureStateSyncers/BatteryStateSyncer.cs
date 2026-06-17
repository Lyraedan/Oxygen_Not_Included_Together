using System;
using System.Collections.Generic;
using System.Text;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Patches.World;
using UnityEngine;

namespace ONI_Together.Networking.Components.StructureStateSyncers
{
    public class BatteryStateSyncer : StructureSyncerBase
    {
        private Battery battery;
        private BatteryTracker batteryTracker;

        protected override void Initialize()
        {
            battery = GetComponent<Battery>();
            batteryTracker = GetComponent<BatteryTracker>();
            checkOptionalsValuesForChanges = false;
            cullByViewport = false;
        }

        protected override void SampleState(out Variant value, out bool active, out Dictionary<string, Variant> optionalValues)
        {
            value = battery?.JoulesAvailable ?? 0f;
            active = false;
            optionalValues = new Dictionary<string, Variant>();
        }

        protected override void ApplyState(StructureStatePacket packet)
        {
            battery.joulesAvailable = packet.Value.Float;
            RefreshBatteryTracker();
            UpdateBatteryMeter(battery, packet.Value.Float);
        }

        private void UpdateBatteryMeter(Battery battery, float joules)
        {
            try
            {
                var meter = battery.meter;
                if (meter == null) return;

                if (battery.capacity <= 0f) return;

                float percent = Mathf.Clamp01(joules / battery.capacity);
                meter.SetPositionPercent(percent);
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[BatteryStateSyncer] Meter update failed: {ex}");
            }
        }

        private void RefreshBatteryTracker() 
        {
            if (batteryTracker == null) return;

            using var allowClientRefresh = BatteryTrackerPatch.AllowClientRefresh();
            batteryTracker.UpdateData();
        }

        protected override bool ShouldForceSync()
        {
            return false;
        }
    }
}
