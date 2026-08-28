using System.IO;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.OxySync;
using Shared.Profiling;

namespace ONI_Together.Networking.OxySync.Packets
{
    public class ClientRpcPacket : IPacket
    {
        public int NetId;
        public int MethodHash;
        public byte[] Args;
        public ulong TargetPlayerId;

        public ClientRpcPacket()
        {
            Args = System.Array.Empty<byte>();
            TargetPlayerId = ulong.MaxValue;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(NetId);
            writer.Write(MethodHash);
            writer.Write(Args.Length);
            writer.Write(Args);
            writer.Write(TargetPlayerId);
        }

        public void Deserialize(BinaryReader reader)
        {
            NetId = reader.ReadInt32();
            MethodHash = reader.ReadInt32();
            int len = reader.ReadInt32();
            Args = reader.ReadBytes(len);
            TargetPlayerId = reader.ReadUInt64();
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (MultiplayerSession.IsHost) return;

            if (TargetPlayerId != ulong.MaxValue && TargetPlayerId != MultiplayerSession.LocalUserID)
                return;

            if (!NetworkIdentityRegistry.TryGetComponent<NetworkBehaviour>(NetId, out var behaviour))
                return;

            behaviour.InvokeClientRpc(MethodHash, Args);
        }
    }
}
