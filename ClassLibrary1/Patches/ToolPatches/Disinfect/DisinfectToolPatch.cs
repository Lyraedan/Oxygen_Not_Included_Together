using HarmonyLib;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Tools.Disinfect;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.ToolPatches.Disinfect
{
[HarmonyPatch(typeof(DisinfectTool), "OnDragTool")]
public class DisinfectToolPatch
{
    [HarmonyPrefix]
    public static void Prefix(int cell, int distFromOrigin)
    {
        using var _ = Profiler.Scope();

        if (!MultiplayerSession.InSession)
            return;

        if (DisinfectPacket.ProcessingIncoming)
            return;

        PacketSender.SendToAllOtherPeers(new DisinfectPacket { cell = cell, distFromOrigin = distFromOrigin });
    }
}
}