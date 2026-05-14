using System;
using System.Collections.Generic;
using System.Text;
using ONI_MP.Misc;
using ONI_MP.Networking.Packets.World;
using UnityEngine;

namespace ONI_MP.Networking.Components.StructureStateSyncers
{
    public class GenericGeneratorSyncer : StructureSyncerBase
    {
        private Generator generator;

        protected override void Initialize()
        {
            generator = new Generator();
        }

        protected override void SampleState(out Variant value, out bool active, out Variant[] optionalValues)
        {
            value = generator?.JoulesAvailable ?? 0f;
            active = false;
            optionalValues = [];
        }

        protected override void ApplyState(StructureStatePacket packet)
        {
            if (generator == null)
                return;

            generator.AssignJoulesAvailable(packet.Value.Float);
        }

        protected override void BuildPacket(StructureStatePacket packet) { }

        protected override bool ShouldForceSync()
        {
            return false;
        }
    }
}
