using System.Collections.Generic;
using System.IO;
using ONI_Together.Misc;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.OxySync;
using Shared.Profiling;

namespace ONI_Together.Networking.OxySync.Packets
{
    public class SyncVarBatchPacket : IPacket
    {
        public int NetId;
        public int BehaviourId;
        public int Count;
        public int[] FieldHashes;
        public Variant[] Values;
        public long Timestamp;

        public SyncVarBatchPacket()
        {
            FieldHashes = System.Array.Empty<int>();
            Values = System.Array.Empty<Variant>();
        }

        public SyncVarBatchPacket(int netId, int behaviourId, List<(int Hash, Variant Value)> updates)
        {
            NetId = netId;
            BehaviourId = behaviourId;
            Count = updates.Count;
            FieldHashes = new int[updates.Count];
            Values = new Variant[updates.Count];
            for (int i = 0; i < updates.Count; i++)
            {
                FieldHashes[i] = updates[i].Hash;
                Values[i] = updates[i].Value;
            }
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(NetId);
            writer.Write(BehaviourId);
            writer.Write(Timestamp);
            writer.Write(Count);
            for (int i = 0; i < Count; i++)
            {
                writer.Write(FieldHashes[i]);
                Values[i].Write(writer);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            NetId = reader.ReadInt32();
            BehaviourId = reader.ReadInt32();
            Timestamp = reader.ReadInt64();
            Count = reader.ReadInt32();
            FieldHashes = new int[Count];
            Values = new Variant[Count];
            for (int i = 0; i < Count; i++)
            {
                FieldHashes[i] = reader.ReadInt32();
                Values[i] = Variant.Read(reader);
            }
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (MultiplayerSession.IsHost) return;

            OxySyncManager.TryGet(NetId, BehaviourId, out NetworkBehaviour behaviour);

            if (behaviour == null && !NetworkIdentityRegistry.TryGetComponent<NetworkBehaviour>(NetId, out behaviour))
                return;

            var fields = behaviour.SyncVarFields;
            for (int i = 0; i < Count; i++)
            {
                int hash = FieldHashes[i];
                var val = Values[i];

                for (int j = 0; j < fields.Count; j++)
                {
                    if (fields[j].Hash == hash)
                    {
                        var obj = VariantHelper.VariantToObject(val, fields[j].Info.FieldType);
                        behaviour.ApplySyncVar(hash, obj, Timestamp);
                        break;
                    }
                }
            }
        }
    }
}