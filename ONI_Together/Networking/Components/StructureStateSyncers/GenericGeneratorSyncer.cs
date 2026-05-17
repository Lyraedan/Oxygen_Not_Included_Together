using System;
using System.Collections.Generic;
using System.Text;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.World;
using UnityEngine;

namespace ONI_Together.Networking.Components.StructureStateSyncers
{
    public class GenericGeneratorSyncer : StructureSyncerBase
    {
        private Generator generator;

        protected override void Initialize()
        {
            generator = new Generator();
        }

        protected override void SampleState(out Variant value, out bool active, out List<Variant> optionalValues)
        {
            value = generator?.JoulesAvailable ?? 0f;
            active = false;
            optionalValues = new List<Variant>();
        }

        protected override void ApplyState(StructureStatePacket packet)
        {
            if (generator == null)
                return;

            generator.AssignJoulesAvailable(packet.Value.Float);
        }

        protected override bool ShouldForceSync()
        {
            return false;
        }
    }
}
