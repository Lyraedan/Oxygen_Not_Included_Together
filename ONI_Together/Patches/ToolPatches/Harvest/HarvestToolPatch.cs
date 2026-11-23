using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Tools.Harvest;
using Shared.Profiling;

namespace ONI_Together.Patches.ToolPatches.Harvest;

[HarmonyPatch(typeof(HarvestTool), nameof(HarvestTool.OnDragTool))]
public class HarvestToolPatch
{
    private static void Postfix(int cell, int distFromOrigin)
    {
        using var _ = Profiler.Scope();

        if (!MultiplayerSession.InActiveSession)
            return;

        if (HarvestToolPacket.ProcessingIncoming)
            return;

        PacketSender.SendToAllOtherPeers(new HarvestToolPacket { cell = cell, distFromOrigin = distFromOrigin });
    }
}