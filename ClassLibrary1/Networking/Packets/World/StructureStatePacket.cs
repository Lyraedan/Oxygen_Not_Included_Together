using ONI_MP.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using ONI_MP.Networking.Components;
using static TUNING.NOISE_POLLUTION;
using ONI_MP.Misc;
using UnityEngine;
using ONI_MP.Networking.Components.StructureStateSyncers;
using static ONI_MP.STRINGS.UI.MP_OVERLAY;

namespace ONI_MP.Networking.Packets.World
{
	public class StructureStatePacket : IPacket
	{

        public int NetId;
        public int Cell;
		public Variant Value; // Joules for Battery, Progress for others

		public Variant[] OptionalValues = []; // Extra things (such as EnergyGenerator mass, storage amount etc)

		public bool IsActive; // Operational active state

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

            writer.Write(NetId);
			writer.Write(Cell);
            Value.Write(writer);
			writer.Write(IsActive);

            writer.Write(OptionalValues.Length);
            for (int i = 0; i < OptionalValues.Length; i++)
            {
                OptionalValues[i].Write(writer);
            }
        }

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

            NetId = reader.ReadInt32();
			Cell = reader.ReadInt32();
			Value = Variant.Read(reader);
			IsActive = reader.ReadBoolean();

            int length = reader.ReadInt32();
            OptionalValues = new Variant[length];
            for (int i = 0; i < length; i++)
            {
                OptionalValues[i] = Variant.Read(reader);
            }
        }

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost) return;

			// Handled by StructureStateSyncer on client
            if(NetworkIdentityRegistry.TryGet(NetId, out var identity))
            {
                var syncers = identity.GetComponents<StructureSyncerBase>();
                foreach (var syncer in syncers)
                {
                    syncer.HandlePacket(this);
                }
                /*
                if(identity.TryGetComponent<StructureSyncerBase>(out var syncer))
                {
                    syncer.HandlePacket(this);
                }
                */
            }
		}

        public static bool VariantValueChanged(Variant a, Variant b, float epsilon = 0.01f)
        {
            if (a.Type != b.Type) return true;
            switch (a.Type)
            {
                case Variant.TypeCode.Float:
                    if (Mathf.Abs(a.Float - b.Float) > epsilon) return true;
                    break;
                case Variant.TypeCode.Int:
                    if (a.Int != b.Int) return true;
                    break;
                case Variant.TypeCode.Byte:
                    if (a.Byte != b.Byte) return true;
                    break;
                case Variant.TypeCode.String:
                    if (a.String != b.String) return true;
                    break;
                case Variant.TypeCode.Boolean:
                    if (a.Boolean != b.Boolean) return true;
                    break;
            }

            return false;
        }

        public static bool OptionalValuesChanged(Variant[] a, Variant[] b)
        {
            if (a == null && b == null) return false;
            if (a == null || b == null) return true;
            if (a.Length != b.Length) return true;

            for (int i = 0; i < a.Length; i++)
            {
                bool changed = VariantValueChanged(a[i], b[i]);
                if (changed) return true;
            }
            return false;
        }
    }
}
