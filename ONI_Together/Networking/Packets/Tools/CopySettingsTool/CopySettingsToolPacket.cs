using System.IO;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Tools.CopySettingsTool;

public class CopySettingsToolPacket : IPacket, IClientRelayable
{
    private int NetID;
    private int Cell;

    public CopySettingsToolPacket()
    {
    }

    public CopySettingsToolPacket(int netID, int cell)
    {
        using var _ = Profiler.Scope();

        NetID = netID;
        Cell  = cell;
    }

    public void Serialize(BinaryWriter writer)
    {
        using var _ = Profiler.Scope();

        writer.Write(NetID);
        writer.Write(Cell);
    }

    public void Deserialize(BinaryReader reader)
    {
        using var _ = Profiler.Scope();

        NetID = reader.ReadInt32();
        Cell  = reader.ReadInt32();
    }

    public void OnDispatched()
    {
        using var _ = Profiler.Scope();

        NetworkIdentity identity;
        if (!NetworkIdentityRegistry.TryGet(NetID, out identity) 
            || identity.gameObject == null 
            || !identity.TryGetComponent<CopyBuildingSettings>(out var sourceSettings)
            || !identity.TryGetComponent<KPrefabID>(out var sourceId))
            return;

		KPrefabID targetId = CopyBuildingSettings.ResolveTarget(CopyBuildingSettings.ResolveLayer(identity.gameObject), Cell);
        if (targetId == null)
            return;

		CopyBuildingSettings.ApplyCopy(targetId, identity.gameObject, sourceId, sourceSettings);
        Game.Instance.userMenu.Refresh(targetId.gameObject);
    }
}
