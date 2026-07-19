using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Components.StructureStateSyncers;
using ONI_Together.Networking.Packets.Architecture;

namespace ONI_Together.Networking.Packets.World;

public class StructureStateRequestPacket : IPacket
{
    private static int _rejectedPackets;
    public ulong RequesterId;
    public int NetId;
    internal static bool ShouldAccept(ulong requesterId, DispatchContext context) =>
        requesterId != 0 && !context.SenderIsHost && SyncBarrier.SenderMatches(requesterId, context.SenderId);

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(RequesterId);
        writer.Write(NetId);
    }

    public void Deserialize(BinaryReader reader)
    {
        RequesterId = reader.ReadUInt64();
        NetId = reader.ReadInt32();
    }

    public void OnDispatched()
    {
        if (!MultiplayerSession.IsHost) return;
        if (!ShouldAccept(RequesterId, PacketHandler.CurrentContext))
        {
            int rejected = ++_rejectedPackets;
            if (rejected <= 5 || rejected % 100 == 0)
                DebugConsole.LogWarning($"[StructureStateRequestPacket] Rejected requester {RequesterId} from {PacketHandler.CurrentContext.SenderId}, host={PacketHandler.CurrentContext.SenderIsHost} (#{rejected})");
            return;
        }

        if (!NetworkIdentityRegistry.TryGet(NetId, out var identity))
        {
            LogicStateSyncer.Instance?.SendStateToClient(RequesterId, NetId);
            return;
        }

        var syncers = identity.GetComponents<StructureSyncerBase>();
        foreach (var syncer in syncers)
            syncer.SendStateToClient(RequesterId);

        LogicStateSyncer.Instance?.SendStateToClient(RequesterId, NetId);
    }
}
