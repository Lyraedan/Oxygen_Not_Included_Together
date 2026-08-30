using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Tools.Disinfect;
using Shared.Profiling;

[HarmonyPatch(typeof(DisinfectTool), "OnDragTool")]
public class DisinfectToolPatch
{
    [HarmonyPrefix]
    public static void Prefix(int cell, int distFromOrigin)
    {
        using var _ = Profiler.Scope();

        if (!MultiplayerSession.InActiveSession)
            return;

        if (DisinfectPacket.ProcessingIncoming)
            return;

        PacketSender.SendToAllOtherPeers(new DisinfectPacket { cell = cell, distFromOrigin = distFromOrigin });
    }
}