using System.IO;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.OxySync;
using Shared.Profiling;

namespace ONI_Together.Networking.OxySync.Packets
{
    public class CommandPacket : IPacket
    {
        public int NetId;
        public int BehaviourId;
        public int MethodHash;
        public byte[] Args;

        public CommandPacket()
        {
            Args = System.Array.Empty<byte>();
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(NetId);
            writer.Write(BehaviourId);
            writer.Write(MethodHash);
            writer.Write(Args.Length);
            writer.Write(Args);
        }

        public void Deserialize(BinaryReader reader)
        {
            NetId = reader.ReadInt32();
            BehaviourId = reader.ReadInt32();
            MethodHash = reader.ReadInt32();
            int len = reader.ReadInt32();
            Args = reader.ReadBytes(len);
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (!MultiplayerSession.IsHost) return;

            OxySyncManager.TryGet(NetId, BehaviourId, out NetworkBehaviour behaviour);

            if (behaviour == null && !NetworkIdentityRegistry.TryGetComponent<NetworkBehaviour>(NetId, out behaviour))
                return;

            behaviour.InvokeCommand(MethodHash, Args);
        }
    }
}