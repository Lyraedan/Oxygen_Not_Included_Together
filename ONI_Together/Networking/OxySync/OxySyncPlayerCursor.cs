using ONI_Together.Misc;
using Shared.OxySync;
using Shared.OxySync.Attributes;

namespace ONI_Together.Networking.OxySync;

[FixedInterestGroup]
public class OxySyncPlayerCursor : NetworkBehaviour
{
    [SyncVar(Hook = nameof(OnPlayerNameChanged))] public string PlayerName = "Unknown Player";
    public System.Action<string> OnNameChanged;
    
    public override void OnSpawn()
    {
        base.OnSpawn();
        InterestGroup = -1;
    }

    [Command]
    public void CmdRequestName(ulong targetId)
    {
        CallTargetRpc(targetId, TargetRpcOnNameRequested);
    }

    [Server]
    [Command]
    public void CmdHandleNameResponse(string playerName)
    {
        PlayerName = playerName;
    }

    [TargetRpc]
    public void TargetRpcOnNameRequested()
    {
        CallCommand(CmdHandleNameResponse, Utils.GetLocalPlayerName());
    }

    private void OnPlayerNameChanged(string oldName, string newName)
    {
        OnNameChanged.Invoke(newName);
    }
}