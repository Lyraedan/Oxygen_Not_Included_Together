using System;
using System.Collections.Generic;
using System.Text;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.World;
using UnityEngine;

namespace ONI_Together.Networking.Components.StructureStateSyncers
{
    public class EnergyGeneratorSyncer : StructureSyncerBase
    {
        private Generator generator;
        private EnergyGenerator energyGen;
        private Storage storage;

        protected override void Initialize()
        {
            generator = GetComponent<Generator>();
            energyGen = GetComponent<EnergyGenerator>();
            storage = energyGen?.storage;
            cullByViewport = false;
        }

        protected override void SampleState(out Variant value, out bool active, out Dictionary<string, Variant> optionalValues)
        {
            value = generator?.JoulesAvailable ?? 0f;
            active = false;

            optionalValues = new Dictionary<string, Variant>();

            if (energyGen == null || !energyGen.hasMeter || storage == null)
                return;

            var inputItem = energyGen.formula.inputs[0];
            optionalValues["input_mass"] = storage.GetMassAvailable(inputItem.tag);
            optionalValues["max_stored_mass"] = inputItem.maxStoredMass;
        }

        protected override void ApplyState(StructureStatePacket packet)
        {
            if (generator == null) return;

            if (packet.OptionalValues.TryGetValue("input_mass", out var massVar) &&
                packet.OptionalValues.TryGetValue("max_stored_mass", out var maxVar) &&
                energyGen != null)
            {
                energyGen.meter?.SetPositionPercent(Mathf.Clamp01(massVar.Float / maxVar.Float));
            }

            generator.AssignJoulesAvailable(packet.Value.Float);
        }

        protected override bool ShouldForceSync()
        {
            return false;
        }
    }
}
