using System.IO;
using ONI_Together.Networking.Packets.Architecture;

namespace ONI_Together.Networking.Packets.World;

public class DespawnEntityPacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
{
    public int NetId;
    public ulong Revision;

    public DespawnEntityPacket() { }

    public DespawnEntityPacket(int netId)
    {
        NetId = netId;
        Revision = NetworkIdentityRegistry.EndLifecycle(netId);
    }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(NetId);
        writer.Write(Revision);
    }

    public void Deserialize(BinaryReader reader)
    {
        NetId = reader.ReadInt32();
        Revision = reader.ReadUInt64();
		if (NetId == 0 || Revision == 0)
			throw new InvalidDataException("Invalid despawn lifecycle metadata");
    }

    public void OnDispatched()
    {
        ulong lastRevision = NetworkIdentityRegistry.GetLastLifecycleRevision(NetId);
        if (!ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost,
                lastRevision, Revision)
            || !NetworkIdentityRegistry.TryAcceptLifecycleRevision(NetId, Revision, tombstone: true))
            return;
		StorageItemPacket.CancelPending(NetId);
		GroundItemPickedUpPacket.CancelPending(NetId);
		SpawnPrefabPacket.CancelPendingBinding(NetId);
		DuplicantDeathStatePacket.CancelPending(NetId);

        if (!NetworkIdentityRegistry.TryGet(NetId, out var identity))
            return;

        Util.KDestroyGameObject(identity.gameObject);
    }

    internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
        => !localIsHost && senderIsHost;

    internal static bool ShouldApply(bool localIsHost, bool senderIsHost, ulong lastRevision, ulong incomingRevision)
        => ShouldApply(localIsHost, senderIsHost)
           && NetworkIdentityRegistry.IsNewerRevision(lastRevision, incomingRevision);
}
