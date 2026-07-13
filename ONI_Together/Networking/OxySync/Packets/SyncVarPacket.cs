using System.IO;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.OxySync;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Packets
{
    public class SyncVarPacket : IPacket
    {
        public int NetId;
        public int FieldHash;
        public Variant Value;
        public long Timestamp;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(NetId);
            writer.Write(FieldHash);
            Value.Write(writer);
            writer.Write(Timestamp);
        }

        public void Deserialize(BinaryReader reader)
        {
            NetId = reader.ReadInt32();
            FieldHash = reader.ReadInt32();
            Value = Variant.Read(reader);
            Timestamp = reader.ReadInt64();
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (MultiplayerSession.IsHost) return;

            if (!NetworkIdentityRegistry.TryGetComponent<NetworkBehaviour>(NetId, out var behaviour))
                return;

            var fields = behaviour.SyncVarFields;
            for (int i = 0; i < fields.Count; i++)
            {
                if (fields[i].Hash == FieldHash)
                {
                    var obj = VariantToObject(Value, fields[i].Info.FieldType);
                    behaviour.ApplySyncVar(FieldHash, obj, Timestamp);
                    return;
                }
            }
        }

        internal static object VariantToObject(Variant v, System.Type targetType)
        {
            if (targetType == typeof(int)) return v.Int;
            if (targetType == typeof(float)) return v.Float;
            if (targetType == typeof(byte)) return v.Byte;
            if (targetType == typeof(string)) return v.String ?? string.Empty;
            if (targetType == typeof(bool)) return v.Boolean;
            if (targetType == typeof(Vector3)) return v.Vector3;
            if (targetType == typeof(Vector2)) return v.Vector2;
            if (targetType == typeof(byte[])) return v.ByteArray ?? System.Array.Empty<byte>();
            if (targetType == typeof(Quaternion)) return v.Quaternion;
            return v.String ?? string.Empty;
        }
    }
}
