using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Tools.Capture;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.ToolPatches.Capture;

[HarmonyPatch(typeof(CaptureTool), nameof(CaptureTool.OnDragComplete))]
public class CaptureToolPatch
{
    private static void Postfix(Vector3 downPos, Vector3 upPos, CaptureTool __instance)
    {
        using var _ = Profiler.Scope();

        if (!MultiplayerSession.InActiveSession)
            return;

        Vector2 min_object = __instance.GetRegularizedPos(Vector2.Min(downPos, upPos), true);
        Vector2 max_object = __instance.GetRegularizedPos(Vector2.Max(downPos, upPos), false);

        PacketSender.SendToAllOtherPeers(new CaptureToolPacket(min_object, max_object));
    }
}