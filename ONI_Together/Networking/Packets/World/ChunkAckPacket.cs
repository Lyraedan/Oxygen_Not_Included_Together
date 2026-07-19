using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using Steamworks;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
    /// <summary>
    /// Client sends ACK to confirm that it received a specific chunk
    /// Server uses this to detect lost chunks and resend only the necessary ones
    /// </summary>
    public class ChunkAckPacket : IPacket
    {
        private static int _rejectedPackets;
        public int SequenceNumber;       // ID of chunk that was received (0, 1, 2, 3...)
        public string TransferId;        // Transfer ID (same as SecureTransferPacket)
        public ulong ClientSteamID;   // Who is sending the ACK
        internal static bool ShouldAccept(ulong clientId, DispatchContext context) =>
            clientId != 0 && !context.SenderIsHost && SyncBarrier.SenderMatches(clientId, context.SenderId);

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();

            writer.Write(SequenceNumber);
            writer.Write(TransferId);
            writer.Write(ClientSteamID);
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();

            SequenceNumber = reader.ReadInt32();
            TransferId = reader.ReadString();
            ClientSteamID = reader.ReadUInt64();
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            // Only server processes ACKs
            if (!MultiplayerSession.IsHost)
                return;
            if (!ShouldAccept(ClientSteamID, PacketHandler.CurrentContext))
            {
                int rejected = ++_rejectedPackets;
                if (rejected <= 5 || rejected % 100 == 0)
                    DebugConsole.LogWarning($"[ChunkAck] Rejected client {ClientSteamID} from {PacketHandler.CurrentContext.SenderId}, host={PacketHandler.CurrentContext.SenderIsHost} (#{rejected})");
                return;
            }

            DebugConsole.Log($"[ChunkAck] Received ACK {SequenceNumber} from {ClientSteamID} for transfer {TransferId}");

            // Inform transfer system about the ACK
            SaveFileTransferManager.HandleChunkAck(ClientSteamID, TransferId, SequenceNumber);
        }
    }
}
