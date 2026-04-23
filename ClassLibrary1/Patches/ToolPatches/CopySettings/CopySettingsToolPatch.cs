using HarmonyLib;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.Tools.CopySettingsTool;
using Shared.Profiling;
using UnityEngine;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.ToolPatches.CopySettings;

[HarmonyPatch(typeof(CopySettingsTool), nameof(CopySettingsTool.OnDragTool))]
public class CopySettingsToolPatch
{
    private static void Postfix(int cell, CopySettingsTool __instance)
    {
        using var _ = Profiler.Scope();

        if (!MultiplayerSession.InSession)
            return;

        GameObject src = __instance.sourceGameObject;
        if (src == null)
            return;

        NetworkIdentity identity = src.GetComponent<NetworkIdentity>();
        if (identity == null)
            return;

        PacketSender.SendToAllOtherPeers(new CopySettingsToolPacket(identity.NetId, cell));
    }
}