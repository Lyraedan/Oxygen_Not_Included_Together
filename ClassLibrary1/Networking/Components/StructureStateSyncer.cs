using System;
using System.Collections.Generic;
using ONI_MP.DebugTools;
using ONI_MP.Misc;
using ONI_MP.Networking.Packets.World;
using ONI_MP.Patches.World;
using Shared.Profiling;
using TemplateClasses;
using UnityEngine;

using static EnergyGenerator;
using static ONI_MP.Networking.Packets.World.StructureStatePacket;

namespace ONI_MP.Networking.Components
{
	public class StructureStateSyncer : KMonoBehaviour
	{
		public enum StructureType
		{
			UNCATEGORIZED,
			BATTERY,          // Battery
			GENERATOR,        // ManualGenerator, EnergyGenerator etc
            STORAGE_CONTAINER // StorageLocker etc
		}

        public enum GeneratorType
        {
            UNKNOWN,
            MANUAL,
            ENERGY,
            MODULE,
            STATERPILLER
        }

        public struct StorageData
        {
            public int PrefabTagHash;
            public float Mass;
            public float Units;
            public float Temperature;
            public byte DiseaseIdx;
            public int DiseaseCount;
        }

        private float sendInterval = 0.5f; // Sync every 500ms
		private float timer;

		private Battery battery;
		private Generator generator;
        private Storage storage;

        private Operational operational;
		private int cell;

		private Variant lastSentValue;
		private bool lastSentActive;
        private Variant[] lastOptionalValues;

		// Grace period
		private bool _initialized = false;
		private float _initializationTime;
		private const float INITIAL_DELAY = 5f;

		public StructureType structureType = StructureType.UNCATEGORIZED;

        // Generator specific
        public GeneratorType generatorType = GeneratorType.UNKNOWN;
        public object generatorInstance;

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();

			base.OnSpawn();
            cell = Grid.PosToCell(this);
            operational = GetComponent<Operational>();
        }

		public void InitalizeAsStructure(StructureType structureType)
		{
			this.structureType = structureType;
			switch(structureType)
			{
				case StructureType.BATTERY:
					battery = GetComponent<Battery>();
					break;
				case StructureType.GENERATOR:
					generator = GetComponent<Generator>();
                    generatorType = DetermineGeneratorType(this.gameObject, out var gen);
                    generatorInstance = gen;
                    switch(generatorType)
                    {
                        case GeneratorType.ENERGY:
                            EnergyGenerator eg = generatorInstance as EnergyGenerator;
                            storage = eg.storage;
                            break;
                    }
                    break;
                case StructureType.STORAGE_CONTAINER:
                    storage = GetComponent<Storage>();
                    break;
                case StructureType.UNCATEGORIZED:
                default:
                    break;
			}
		}

		private void Update()
		{
			using var _ = Profiler.Scope();

            if (!MultiplayerSession.InSession)
                return;

			if (MultiplayerSession.IsHost)
			{
				// Skip if no clients connected
			  //  if (!MultiplayerSession.SessionHasPlayers)
					//return;

				// Grace period after world load
				if (!_initialized)
				{
					_initializationTime = Time.unscaledTime;
					_initialized = true;
					return;
				}

				if (Time.unscaledTime - _initializationTime < INITIAL_DELAY)
					return;

				HostUpdate();
			}
		}

        private void HostUpdate()
        {
            using var _ = Profiler.Scope();

            try
            {
                timer += Time.unscaledDeltaTime;
                if (timer < sendInterval) return;
                timer = 0f;

                Variant currentValue = 0f;
                bool currentActive = false;
                Variant[] optionalValues = [];

                if (operational != null)
                    currentActive = operational.IsActive;

                switch (structureType)
                {
                    case StructureType.BATTERY:
                        if (battery != null)
                        {
                            currentValue = battery.JoulesAvailable;
                        }
                        break;

                    case StructureType.GENERATOR:
                        if (generator != null)
                        {
                            switch(generatorType)
                            {
                                case GeneratorType.ENERGY:
                                    EnergyGenerator gen = generatorInstance as EnergyGenerator;
                                    if (gen != null)
                                    {
                                        // Coal generator
                                        if (gen.hasMeter)
                                        {
                                            //EncodeStorageContents(storage, out var storageContents);

                                            InputItem inputItem = gen.formula.inputs[0];
                                            float mass = storage.GetMassAvailable(inputItem.tag);
                                            float storedMass = inputItem.maxStoredMass;

                                            optionalValues = new Variant[2/* + storageContents.Length*/];
                                            optionalValues[0] = mass;
                                            optionalValues[1] = storedMass;

                                            //Array.Copy(storageContents, optionalValues, storageContents.Length);
                                        }
                                    }
                                    break;
                                default:
                                    break;
                            }
                            currentValue = generator.JoulesAvailable;
                        }
                        break;

                    case StructureType.STORAGE_CONTAINER:
                        if (storage != null)
                        {
                            currentValue = storage.MassStored();
                            EncodeStorageContents(storage, out var contents);
                            optionalValues = contents;
                        }
                        break;

                    case StructureType.UNCATEGORIZED:
                    default:
                        break;
                }

                // Sync if changed significantly
                if (VariantValueChanged(currentValue, lastSentValue) ||
                    currentActive != lastSentActive ||
                    OptionalValuesChanged(optionalValues, lastOptionalValues))
                {
                    lastSentValue = currentValue;
                    lastSentActive = currentActive;
                    lastOptionalValues = optionalValues;

                    var packet = new StructureStatePacket
                    {
                        Cell = cell,
                        Value = currentValue,
                        IsActive = currentActive,
                        StructureType = structureType,
                        GeneratorType = generatorType,
                        OptionalValues = optionalValues
                    };

                    PacketSender.SendToAllClients(packet, PacketSendMode.Unreliable);
                }
            }
            catch (System.Exception)
            {
                // Silent fail - Structure may not be ready
            }
        }

        public static void HandlePacket(StructureStatePacket packet)
		{
			using var _ = Profiler.Scope();
			if (!Grid.IsValidCell(packet.Cell)) return;

			GameObject go = Grid.Objects[packet.Cell, (int)ObjectLayer.Building];
			if (go == null) return;

            switch(packet.StructureType)
            {
                case StructureType.BATTERY:
                    ApplyBatteryState(go, packet);
                    break;
                case StructureType.GENERATOR:
                    ApplyGeneratorState(go, packet);
                    break;
                case StructureType.STORAGE_CONTAINER:
                    ApplyStorageState(go, packet);
                    break;
                case StructureType.UNCATEGORIZED:
                default:
                    break;
            }
            ApplyOperationalState(go, packet);
		}

        #region Battery
        private static void ApplyBatteryState(GameObject go, StructureStatePacket packet)
        {
            var battery = go.GetComponent<Battery>();
            if (battery == null) return;

            battery.joulesAvailable = packet.Value.Float;
            RefreshBatteryTracker(go);
			UpdateBatteryMeter(battery, packet.Value.Float);
        }

        private static void UpdateBatteryMeter(Battery battery, float joules)
		{
			try
			{
                var meter = battery.meter;
                if (meter == null) return;

                if (battery.capacity <= 0f) return;

                float percent = Mathf.Clamp01(joules / battery.capacity);
                meter.SetPositionPercent(percent);
            } catch(Exception ex)
			{
                DebugConsole.LogError($"[StructureStateSyncer] Meter update failed: {ex}");
            }
		}

        private static void RefreshBatteryTracker(GameObject go)
        {
            var tracker = go.GetComponent<BatteryTracker>();
            if (tracker == null) return;

            using var allowClientRefresh = BatteryTrackerPatch.AllowClientRefresh();
            tracker.UpdateData();
        }
        #endregion

        #region Generator
        public static void ApplyGeneratorState(GameObject go, StructureStatePacket packet)
        {
            var generator = go.GetComponent<Generator>();
            if(generator == null) return;

            DebugConsole.Log("Apply generator state passed check 1 with: " + packet.OptionalValues.Length + " optionals");
            switch(packet.GeneratorType)
            {
                case GeneratorType.ENERGY:
                    if (packet.OptionalValues.Length > 0)
                    {
                        EnergyGenerator gen = generator as EnergyGenerator;
                        if (gen != null)
                        {
                            float mass = packet.OptionalValues[0].Float;
                            float storedMass = packet.OptionalValues[1].Float;
                            DebugConsole.Log("Applying generator meter update with mass of: " + packet.OptionalValues[0].ToString() + " and stored mass of " + packet.OptionalValues[1].ToString());
                            UpdateEnergyGeneratorMeter(gen, mass, storedMass);

                            // Storage data (WIP)
                            if (packet.OptionalValues.Length > 2)
                            {
                                Variant[] storageData = new Variant[packet.OptionalValues.Length - 2];
                                Array.Copy(packet.OptionalValues, 2, storageData, 0, storageData.Length);
                                RebuildStorageFromData(gen.storage, storageData);
                            }
                        }
                    }
                    break;
                // Don't need to do anything with these yet
                case GeneratorType.MANUAL:
                case GeneratorType.MODULE:
                case GeneratorType.STATERPILLER:
                default:
                    break;
            }

            generator.AssignJoulesAvailable(packet.Value.Float);
        }

        private static void UpdateEnergyGeneratorMeter(EnergyGenerator generator, float mass, float storedMass)
        {
            if (generator.hasMeter)
            {
                float meterPercent = Mathf.Clamp01(mass / storedMass);
                generator.meter.SetPositionPercent(meterPercent);
            }
        }

        private static GeneratorType DetermineGeneratorType(GameObject go, out object generator)
        {
            ManualGenerator manualGenerator = go.GetComponent<ManualGenerator>();
            if (manualGenerator != null)
            {
                generator = manualGenerator;
                return GeneratorType.MANUAL;
            }

            EnergyGenerator energyGenerator = go.GetComponent<EnergyGenerator>();
            if (energyGenerator != null)
            {
                generator = energyGenerator;
                return GeneratorType.ENERGY;
            }

            ModuleGenerator moduleGenerator = go.GetComponent<ModuleGenerator>();
            if (moduleGenerator != null)
            {
                generator = moduleGenerator;
                return GeneratorType.MODULE;
            }

                StaterpillarGenerator statepillerGenerator = go.GetComponent<StaterpillarGenerator>();
            if (moduleGenerator != null)
            {
                generator = statepillerGenerator;
                return GeneratorType.STATERPILLER;
            }

            generator = null;
            return GeneratorType.UNKNOWN;
        }
        #endregion

        #region Storage
        private static void ApplyStorageState(GameObject go, StructureStatePacket packet)
        {
            var storage = go.GetComponent<Storage>();
            if (storage == null || packet.OptionalValues.Length < 2) return;

            RebuildStorageFromData(storage, packet.OptionalValues);
        }

        private static void EncodeStorageContents(Storage storage, out Variant[] optionalValues)
        {
            var items = new List<StorageData>();
            for (int i = 0; i < storage.items.Count; i++)
            {
                if (storage.items[i] == null) continue;
                var pe = storage.items[i].GetComponent<PrimaryElement>();
                if (pe == null || pe.Mass <= 0f) continue;

                if (!storage.items[i].TryGetComponent<KPrefabID>(out var prefabID))
                    continue;
                int tagHash = prefabID.PrefabTag.GetHashCode();

                items.Add(new StorageData
                {
                    PrefabTagHash = tagHash,
                    Mass = pe.Mass,
                    Units = pe.Units,
                    Temperature = pe.Temperature,
                    DiseaseIdx = pe.DiseaseIdx,
                    DiseaseCount = pe.DiseaseCount
                });
            }

            // Header: [capacityKg, count]
            // Per item (6 values): [tagHash, mass, units, temperature, diseaseIdx, diseaseCount]
            optionalValues = new Variant[2 + items.Count * 6];
            optionalValues[0] = storage.capacityKg;
            optionalValues[1] = items.Count;
            int idx = 2;
            foreach (var item in items)
            {
                optionalValues[idx++] = item.PrefabTagHash;
                optionalValues[idx++] = item.Mass;
                optionalValues[idx++] = item.Units;
                optionalValues[idx++] = item.Temperature;
                optionalValues[idx++] = item.DiseaseIdx;
                optionalValues[idx++] = item.DiseaseCount;
            }
        }

        private static void RebuildStorageFromData(Storage storage, Variant[] data)
        {
            if (storage == null || data.Length < 2) return;

            //storage.ConsumeAllIgnoringDisease();
            ClearStorage(storage);

            int count = data[1].Int;
            if (count == 0) return;

            for (int i = 0; i < count; i++)
            {
                int baseIdx = 2 + i * 6;
                if (baseIdx + 6 > data.Length) break;

                int hash = data[baseIdx].Int;
                Tag tag = new Tag(hash);
                float mass = data[baseIdx + 1].Float;
                float temperature = data[baseIdx + 3].Float;
                byte diseaseIdx = data[baseIdx + 4].Byte;
                int diseaseCount = data[baseIdx + 5].Int;

                if (mass <= 0f) continue;

                Element elementByHash = ElementLoader.GetElement(tag);
                if (elementByHash != null)
                    storage.AddElement(elementByHash.id, mass, temperature, diseaseIdx, diseaseCount);
                else
                {
                    var item = Assets.GetPrefab(tag);
                    if(item != null)
                    {
                        var scrapObject = GameUtil.KInstantiate(item, storage.transform.position, Grid.SceneLayer.Ore);
                        if (scrapObject.TryGetComponent<PrimaryElement>(out var scrapObjectElement))
                        {
                            scrapObjectElement.Mass = mass;
                            scrapObjectElement.Temperature = temperature;
                            if(diseaseIdx != byte.MaxValue)
                            {
                                scrapObjectElement.AddDisease(diseaseIdx, diseaseCount, "Multiplayer Sync");
                            }
                        }

                        scrapObject.SetActive(true);
                        storage.Store(scrapObject, true, true);
                    }
                }
            }

            EncodeStorageContents(storage, out Variant[] storageData);
        }

        // Clears the storage items without triggering the events
        private static void ClearStorage(Storage storage)
        {
            for(int i = storage.items.Count - 1; i >= 0; i--)
            {
                storage.items[i].DeleteObject();
            }
            storage.items.Clear();
        }

        #endregion
        private static void ApplyOperationalState(GameObject go, StructureStatePacket packet)
		{
            var operational = go.GetComponent<Operational>();
            if (operational == null) return;

            operational.SetActive(packet.IsActive);
        }
	}
}
