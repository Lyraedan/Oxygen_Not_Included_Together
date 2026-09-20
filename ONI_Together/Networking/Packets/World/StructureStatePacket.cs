using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.OxySync;
using Shared.Profiling;
using UnityEngine;
using ONI_Together.Networking.Components.StructureStateSyncers;
using System.Collections.Generic;

namespace ONI_Together.Networking.Packets.World
{
	public class StructureStatePacket : IPacket
	{

        public int NetId;
        public int Cell;
		public Variant Value; // Joules for Battery, Progress for others

		public Dictionary<string, Variant> OptionalValues = []; // Extra things (such as EnergyGenerator mass, storage amount etc)

		public bool IsActive; // Operational active state

        public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

            writer.Write(NetId);
			writer.Write(Cell);
            Value.Write(writer);
			writer.Write(IsActive);

            using var optMs = new MemoryStream();
            using var optBw = new BinaryWriter(optMs);
            optBw.Write(OptionalValues.Count);
            foreach (var kvp in OptionalValues)
            {
                optBw.Write(kvp.Key);
                kvp.Value.Write(optBw);
            }
            writer.Write((int)optMs.Length);
            writer.Write(optMs.GetBuffer(), 0, (int)optMs.Length);
        }

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

            NetId = reader.ReadInt32();
			Cell = reader.ReadInt32();
			Value = Variant.Read(reader);
			IsActive = reader.ReadBoolean();

            int optLen = reader.ReadInt32();
            byte[] optBlob = reader.ReadBytes(optLen);
            using var optBr = new BinaryReader(new MemoryStream(optBlob));
            int length = optBr.ReadInt32();
            OptionalValues = new Dictionary<string, Variant>(length);
            for (int i = 0; i < length; i++)
            {
                string key = optBr.ReadString();
                OptionalValues[key] = Variant.Read(optBr);
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
                case Variant.TypeCode.ByteArray:
                    if (!ByteArraysEqual(a.ByteArray, b.ByteArray)) return true;
                    break;
            }

            return false;
        }

        private static bool ByteArraysEqual(byte[] a, byte[] b)
        {
            if (a == b) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        public static bool OptionalValuesChanged(Dictionary<string, Variant> a, Dictionary<string, Variant> b)
        {
            if (a == null && b == null) return false;
            if (a == null || b == null) return true;
            if (a.Count != b.Count) return true;

            foreach (var kvp in a)
            {
                if (!b.TryGetValue(kvp.Key, out var bVal)) return true;
                if (VariantValueChanged(kvp.Value, bVal)) return true;
            }
            return false;
        }
    }
}
