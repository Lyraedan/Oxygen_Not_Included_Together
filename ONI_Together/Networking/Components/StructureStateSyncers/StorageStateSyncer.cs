using System;
using System.Collections.Generic;
using System.Text;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.World;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Components.StructureStateSyncers
{
    public class StorageStateSyncer : StructureSyncerBase
    {
		internal static PacketSendMode SnapshotSendMode => PacketSendMode.Reliable;
		protected override PacketSendMode PeriodicSendMode => SnapshotSendMode;

        private Storage storage;
		private StorageSnapshotSync.SnapshotBatch _preparedStorageBatch;
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
			checkOptionalsValuesForChanges = true;
        }


        protected override void SampleState(out Variant value, out bool active, out Dictionary<string, Variant> optionalValues)
        {
            value = storage?.MassStored() ?? 0f;
            active = false;
            optionalValues = new Dictionary<string, Variant>();
            StorageSnapshotSync.Encode(storage, optionalValues);
        }

        protected override void ApplyState(StructureStatePacket packet)
        {
			StorageSnapshotSync.SnapshotBatch batch = _preparedStorageBatch;
			_preparedStorageBatch = null;
			if (batch?.Apply() == true)
				return;
			RequestFreshState();
        }

		protected override bool TryAcceptPacketRevision(StructureStatePacket packet)
		{
			_preparedStorageBatch = null;
			var request = new StorageSnapshotSync.SnapshotRequest
			{
				Storage = storage,
				Data = packet.OptionalValues,
				SnapshotRevision = packet.Revision,
			};
			if (!StorageSnapshotSync.TryPrepareBatch(
				    new[] { request }, out StorageSnapshotSync.SnapshotBatch batch)
			    || !NetworkIdentityRegistry.TryAcceptStorageSnapshotRevision(
				    packet.NetId, packet.Revision))
				return false;
			_preparedStorageBatch = batch;
			return true;
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
