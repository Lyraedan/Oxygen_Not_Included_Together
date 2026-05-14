using System;
using System.Collections.Generic;
using System.Text;
using ONI_MP.DebugTools;
using ONI_MP.Misc;
using ONI_MP.Networking.Packets.World;
using ONI_MP.Patches.World;
using UnityEngine;

namespace ONI_MP.Networking.Components.StructureStateSyncers
{
    public class BatteryStateSyncer : StructureSyncerBase
    {
        private Battery battery;
        private BatteryTracker batteryTracker;

        protected override void Initialize()
        {
            battery = GetComponent<Battery>();
            batteryTracker = GetComponent<BatteryTracker>();
        }

        protected override void SampleState(out Variant value, out bool active, out Variant[] optionalValues)
        {
            value = battery?.JoulesAvailable ?? 0f;
            active = false;
            optionalValues = [];
        }

        protected override void BuildPacket(StructureStatePacket packet)
        {

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
