using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using ONI_MP.Misc;
using ONI_MP.Networking.Packets.World;
using UnityEngine;

namespace ONI_MP.Networking.Components.StructureStateSyncers
{
    public class ToiletSyncer : StructureSyncerBase
    {
        private FlushToilet flushToilet;
        private Toilet outhouseToilet;
        private Storage storage;
        private ConduitConsumer conduitConsumer;

        protected override void Initialize()
        {
            flushToilet = GetComponent<FlushToilet>();
            outhouseToilet = GetComponent<Toilet>();
            storage = GetComponent<Storage>();
            conduitConsumer = GetComponent<ConduitConsumer>();
            checkOptionalsValuesForChanges = false;
        }

        protected override void SampleState(out Variant value, out bool active, out Variant[] optionalValues)
        {
            if (flushToilet != null)
            {
                value = storage?.MassStored() ?? 0f;
            }
            else if (outhouseToilet != null)
            {
                value = outhouseToilet.FlushesUsed;
            }
            else
            {
                value = 0f;
            }
            active = false;
            BuildingUtils.EncodeStorageContents(storage, out optionalValues);
        }

        protected override void BuildPacket(StructureStatePacket packet) { }

        protected override void ApplyState(StructureStatePacket packet)
        {
            if (storage == null || packet.OptionalValues.Length < 2) return;
            BuildingUtils.RebuildStorageFromData(storage, packet.OptionalValues);
            SyncToiletVisualState();
        }

        private void SyncToiletVisualState()
        {
            if (flushToilet != null)
                SyncFlushToilet();
            else if (outhouseToilet != null)
                SyncOuthouse();
        }

        private void SyncFlushToilet()
        {
            float totalWater = 0f, totalWaste = 0f, totalGunk = 0f;

            foreach (var item in storage.items)
            {
                if (item == null) continue;
                var pe = item.GetComponent<PrimaryElement>();
                if (pe == null) continue;
                if (pe.ElementID == SimHashes.Water) totalWater += pe.Mass;
                else if (pe.ElementID == SimHashes.DirtyWater) totalWaste += pe.Mass;
                else if (pe.ElementID == GunkMonitor.GunkElement) totalGunk += pe.Mass;
            }

            bool full = totalWater >= flushToilet.massConsumedPerUse;
            if (conduitConsumer != null)
                conduitConsumer.enabled = !full;

            float fillPct = Mathf.Clamp01(totalWater / flushToilet.massConsumedPerUse);
            float wastePct = Mathf.Clamp01(totalWaste / flushToilet.massEmittedPerUse);
            float gunkPct = Mathf.Clamp01(totalGunk / flushToilet.massEmittedPerUse);

            flushToilet.fillMeter?.SetPositionPercent(fillPct);
            flushToilet.contaminationMeter?.SetPositionPercent(wastePct);
            flushToilet.gunkMeter?.SetPositionPercent(gunkPct);
        }

        private void SyncOuthouse()
        {
            float wasteMass = 0f;
            foreach (var item in storage.items)
            {
                if (item == null) continue;
                var pe = item.GetComponent<PrimaryElement>();
                if (pe == null) continue;
                if (pe.ElementID == outhouseToilet.solidWastePerUse.elementID)
                    wasteMass += pe.Mass;
            }

            int flushes = Mathf.RoundToInt(wasteMass / outhouseToilet.solidWastePerUse.mass);
            outhouseToilet.FlushesUsed = Mathf.Min(flushes, outhouseToilet.maxFlushes);

            var meter = outhouseToilet.meter;
            meter?.SetPositionPercent((float)outhouseToilet.FlushesUsed / outhouseToilet.maxFlushes);
        }

        protected override bool ShouldForceSync()
        {
            return false;
        }
    }
}
