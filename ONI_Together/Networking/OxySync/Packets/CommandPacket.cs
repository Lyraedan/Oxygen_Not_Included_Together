using System.IO;
using ONI_Together.DebugTools;
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
            RpcSerializer.WritePayload(writer, Args);
        }

        public void Deserialize(BinaryReader reader)
        {
            NetId = reader.ReadInt32();
            MethodHash = reader.ReadInt32();
            Args = RpcSerializer.ReadPayload(reader);
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (!MultiplayerSession.IsHost) return;

            var behaviour = OxySyncDispatchResolver.FindCommandBehaviour(NetId, MethodHash);
            if (behaviour == null)
                return;

            // Host-only commands are invoked directly by the host and must never
            // arrive through the client -> host packet path.
            if (behaviour.CommandRequiresHost(MethodHash))
            {
                DebugConsole.LogWarning(
                    $"[OxySync] Rejected remote host-only command {MethodHash} for NetId {NetId}.");
                return;
            }

            behaviour.InvokeCommand(MethodHash, Args);
        }
    }
}
