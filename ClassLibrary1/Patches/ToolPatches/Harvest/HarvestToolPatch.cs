using HarmonyLib;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Tools.Harvest;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.ToolPatches.Harvest;

[HarmonyPatch(typeof(HarvestTool), nameof(HarvestTool.OnDragTool))]
public class HarvestToolPatch
{
    private static void Postfix(int cell, int distFromOrigin)
    {
        using var _ = Profiler.Scope();

        if (!MultiplayerSession.InSession)
            return;

        if (HarvestToolPacket.ProcessingIncoming)
            return;

        PacketSender.SendToAllOtherPeers(new HarvestToolPacket { cell = cell, distFromOrigin = distFromOrigin });
    }
}