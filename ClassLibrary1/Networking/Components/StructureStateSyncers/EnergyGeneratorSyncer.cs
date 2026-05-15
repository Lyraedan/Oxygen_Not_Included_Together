using System;
using System.Collections.Generic;
using System.Text;
using ONI_MP.DebugTools;
using ONI_MP.Misc;
using ONI_MP.Networking.Packets.World;
using UnityEngine;

namespace ONI_MP.Networking.Components.StructureStateSyncers
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
        }

        protected override void SampleState(out Variant value, out bool active, out List<Variant> optionalValues)
        {
            value = generator?.JoulesAvailable ?? 0f;
            active = false;

            if (energyGen == null || !energyGen.hasMeter || storage == null)
            {
                optionalValues = new List<Variant>();
                return;
            }

            var inputItem = energyGen.formula.inputs[0];
            optionalValues = new List<Variant>
            {
                storage.GetMassAvailable(inputItem.tag),
                inputItem.maxStoredMass
            };
        }

        protected override void ApplyState(StructureStatePacket packet)
        {
            if (generator == null) return;

            if (packet.OptionalValues.Count >= 2 && energyGen != null)
            {
                float mass = packet.OptionalValues[0].Float;
                float storedMass = packet.OptionalValues[1].Float;
                energyGen.meter?.SetPositionPercent(Mathf.Clamp01(mass / storedMass));
            }

            generator.AssignJoulesAvailable(packet.Value.Float);
        }

        protected override bool ShouldForceSync()
        {
            return false;
        }
    }
}
