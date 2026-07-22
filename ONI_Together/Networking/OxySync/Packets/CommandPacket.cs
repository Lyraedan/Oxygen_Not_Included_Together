using System.IO;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.OxySync;
using Shared.Profiling;

namespace ONI_Together.Networking.OxySync.Packets
{
    public class CommandPacket : IPacket
    {
        public int NetId;
        public int MethodHash;
        public byte[] Args;

        public CommandPacket()
        {
            Args = System.Array.Empty<byte>();
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(NetId);
            writer.Write(MethodHash);
            writer.Write(Args.Length);
            writer.Write(Args);
        }

        public void Deserialize(BinaryReader reader)
        {
            NetId = reader.ReadInt32();
            MethodHash = reader.ReadInt32();
            int len = reader.ReadInt32();
            Args = reader.ReadBytes(len);
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (!MultiplayerSession.IsHost) return;

            if (!NetworkIdentityRegistry.TryGetComponent<NetworkBehaviour>(NetId, out var behaviour))
                return;

            behaviour.InvokeCommand(MethodHash, Args);
        }
    }
}
