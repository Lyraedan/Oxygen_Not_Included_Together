using Shared.OxySync;
using System.Collections.Generic;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.Networking.Components.StructureStateSyncers
{
    public class GenericGeneratorSyncer : StructureSyncerBase
    {
        private Generator generator;

        protected override void Initialize()
        {
            generator = GetComponent<Generator>();
        }

        protected override void SampleState(out Variant value, out bool active, out Dictionary<string, Variant> optionalValues)
        {
            value = generator?.JoulesAvailable ?? 0f;
            active = false;
            optionalValues = new Dictionary<string, Variant>();
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
