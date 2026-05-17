using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.World;
using UnityEngine;
using static STRINGS.UI.METERS;

namespace ONI_Together.Networking.Components.StructureStateSyncers
{
    public class ToiletSyncer : StructureSyncerBase
    {
        private FlushToilet flushToilet;
        private Toilet outhouseToilet;
        private Storage storage;
        private ConduitConsumer conduitConsumer;
        private KPrefabID prefabID;

        protected override void Initialize()
        {
            flushToilet = GetComponent<FlushToilet>();
            outhouseToilet = GetComponent<Toilet>();
            storage = GetComponent<Storage>();
            conduitConsumer = GetComponent<ConduitConsumer>();
            prefabID = GetComponent<KPrefabID>();
        }

        protected override void SampleState(out Variant value, out bool active, out List<Variant> optionalValues)
        {
            active = false;
            BuildingUtils.EncodeStorageContents(storage, out optionalValues);
            optionalValues.Add(operational?.IsFunctional ?? true);
            if (flushToilet != null)
            {
                value = storage?.MassStored() ?? 0f;
                GetTotalWater(out float totalWater, out float totalWaste, out float totalGunk);
                optionalValues.Add(totalWater);
                optionalValues.Add(totalWaste);
                optionalValues.Add(totalGunk);
                optionalValues.Add(totalGunk);
            }
            else if (outhouseToilet != null)
            {
                value = outhouseToilet.FlushesUsed;
            }
            else
            {
                value = 0f;
            }
        }

        protected override void ApplyState(StructureStatePacket packet)
        {
            if (storage == null) return;
            BuildingUtils.RebuildStorageFromData(storage, packet.OptionalValues);
            SyncToilet(packet);

            // Seems to solve out of order
            if (packet.OptionalValues.Count > 0)
            {
                int itemCount = packet.OptionalValues[1].Int;
                int storageEnd = 2 + itemCount * 6;
                int idx = storageEnd; // skip IsFunctional

                bool functional = packet.OptionalValues[idx].Boolean;
                if (functional)
                {
                    prefabID.AddTag(GameTags.Operational);
                }
                else
                {
                    prefabID.RemoveTag(GameTags.Operational);
                }
            }
        }

        private void SyncToilet(StructureStatePacket packet)
        {
            if (flushToilet != null)
                SyncFlushToilet(packet);
            else if (outhouseToilet != null)
                SyncOuthouse(packet);
        }

        private void GetTotalWater(out float totalWater, out float totalWaste, out float totalGunk)
        {
            totalWater = 0f;
            totalWaste = 0f; 
            totalGunk = 0f;

            foreach (var item in storage.items)
            {
                if (item == null) continue;
                var pe = item.GetComponent<PrimaryElement>();
                if (pe == null) continue;
                if (pe.ElementID == SimHashes.Water) totalWater += pe.Mass;
                else if (pe.ElementID == SimHashes.DirtyWater) totalWaste += pe.Mass;
                else if (pe.ElementID == GunkMonitor.GunkElement) totalGunk += pe.Mass;
            }
        }

        private void SyncFlushToilet(StructureStatePacket packet)
        {
            int itemCount = packet.OptionalValues[1].Int;
            int storageEnd = 2 + itemCount * 6;
            int idx = storageEnd + 1; // skip IsFunctional

            float totalWater = packet.OptionalValues[idx].Float;
            float totalWaste = packet.OptionalValues[idx + 1].Float;
            float totalGunk = packet.OptionalValues[idx + 2].Float;
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

        private void SyncOuthouse(StructureStatePacket packet)
        {
            outhouseToilet.FlushesUsed = packet.Value.Int;
            outhouseToilet.meter?.SetPositionPercent(Mathf.Clamp01((float)outhouseToilet.FlushesUsed / outhouseToilet.maxFlushes));
        }

        protected override bool ShouldForceSync()
        {
            return false;
        }
    }
}
