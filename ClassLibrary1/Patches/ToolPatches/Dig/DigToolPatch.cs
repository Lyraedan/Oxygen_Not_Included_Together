using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Tools.Dig;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.ToolPatches.Dig
{
    [HarmonyPatch(typeof(DigTool), nameof(DigTool.PlaceDig))]
    public static class DigTool_PlaceDig_Patch
    {
        public static void Postfix(int cell, int animationDelay)
        {
            using var _ = Profiler.Scope();

            if (!MultiplayerSession.InSession)
            {
                DebugConsole.LogWarning("[PlaceDig Patch] Skipped: MultiplayerSession.InSession is false");
                return;
            }

            if (DiggablePacket.ProcessingIncoming)
                return;

            PacketSender.SendToAllOtherPeers(new DiggablePacket(cell, animationDelay));
        }
    }
}