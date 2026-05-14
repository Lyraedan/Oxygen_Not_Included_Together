using System;
using System.Collections.Generic;
using System.Text;
using ONI_MP.Misc;
using ONI_MP.Networking.Packets.World;
using UnityEngine;

namespace ONI_MP.Networking.Components.StructureStateSyncers
{
    public class StorageStateSyncer : StructureSyncerBase
    {
        private Storage storage;
        private float temperatureThreshold = 0.3f;
        private float lastStorageTemperature;

        public struct StorageData
        {
            public int PrefabTagHash;
            public float Mass;
            public float Units;
            public float Temperature;
            public byte DiseaseIdx;
            public int DiseaseCount;
        }

        protected override void Initialize()
        {
            storage = GetComponent<Storage>();
            checkOptionalsValuesForChanges = false; // Skip checking optionals for changes since we check temperature in ShouldForceSync and mass is checked via value
        }


        protected override void SampleState(out Variant value, out bool active, out Variant[] optionalValues)
        {
            value = storage?.MassStored() ?? 0f;
            active = false;
            BuildingUtils.EncodeStorageContents(storage, out optionalValues);
        }

        protected override void BuildPacket(StructureStatePacket packet)
        {

        }

        protected override void ApplyState(StructureStatePacket packet)
        {
            if (storage == null || packet.OptionalValues.Length < 2) return;
            BuildingUtils.RebuildStorageFromData(storage, packet.OptionalValues);
        }

        protected override bool ShouldForceSync()
        {
            if (storage == null) return false;

            float currentTemp = GetMaxStorageTemperature(storage);
            if (Mathf.Abs(currentTemp - lastStorageTemperature) > temperatureThreshold)
            {
                lastStorageTemperature = currentTemp;
                return true;
            }
            return false;
        }

        // TODO: Does not scale well in the late game
        private float GetMaxStorageTemperature(Storage storage)
        {
            float max = 0f;
            for (int i = 0; i < storage.items.Count; i++)
            {
                var pe = storage.items[i]?.GetComponent<PrimaryElement>();
                if (pe != null && pe.Temperature > max)
                    max = pe.Temperature;
            }

            return max;
        }
    }
}
