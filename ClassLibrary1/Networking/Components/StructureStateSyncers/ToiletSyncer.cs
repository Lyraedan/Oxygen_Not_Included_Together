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
            optionalValues.Add(operational?.IsFunctional ?? true);
        }

        protected override void ApplyState(StructureStatePacket packet)
        {
            if (storage == null) return;
            BuildingUtils.RebuildStorageFromData(storage, packet.OptionalValues);
            SyncToilet(packet);

            // Seems to solve out of order
            //if (packet.OptionalValues.Count > 0)
            //{
            //    bool functional = packet.OptionalValues[0].Boolean;
            //    if (functional)
            //    {
            //        prefabID.AddTag(GameTags.Operational);
            //    } 
            //    else
            //    {
            //        prefabID.RemoveTag(GameTags.Operational);
            //    }
            //}
        }

        private void SyncToilet(StructureStatePacket packet)
        {
            if (flushToilet != null)
                SyncFlushToilet(packet);
            else if (outhouseToilet != null)
                SyncOuthouse(packet);
        }

        private void SyncFlushToilet(StructureStatePacket packet)
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

        private void SyncOuthouse(StructureStatePacket packet)
        {
            outhouseToilet.FlushesUsed = packet.Value.Int;
            outhouseToilet.meter?.SetPositionPercent((float)outhouseToilet.FlushesUsed / outhouseToilet.maxFlushes);
        }

        protected override bool ShouldForceSync()
        {
            return false;
        }
    }
}
