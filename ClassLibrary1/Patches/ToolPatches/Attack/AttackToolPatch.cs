using HarmonyLib;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Tools.Attack;
using Shared.Profiling;
using UnityEngine;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.ToolPatches.Attack;

[HarmonyPatch(typeof(AttackTool), nameof(AttackTool.OnDragComplete))]
public class AttackToolPatch
{
    private static void Postfix(Vector3 downPos, Vector3 upPos, AttackTool __instance)
    {
        using var _ = Profiler.Scope();

        if (!MultiplayerSession.InSession)
            return;

        Vector2 min_object = __instance.GetRegularizedPos(Vector2.Min(downPos, upPos), true);
        Vector2 max_object = __instance.GetRegularizedPos(Vector2.Max(downPos, upPos), false);

        PacketSender.SendToAllOtherPeers(new AttackToolPacket(min_object, max_object));
    }
}