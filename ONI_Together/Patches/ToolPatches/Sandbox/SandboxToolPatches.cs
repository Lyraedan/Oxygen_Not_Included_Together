using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Tools.Sandbox;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.ToolPatches.Sandbox
{
    internal static class SandboxToolSync
    {
        public static void Send(
            SandboxToolAction action,
            int cell,
            int distanceFromOrigin = 0,
            Vector3 position = default)
        {
            using var _ = Profiler.Scope();

            if (!MultiplayerSession.InActiveSession || SandboxToolPacket.ProcessingIncoming || !Grid.IsValidCell(cell))
                return;

            PacketSender.SendToAllOtherPeers(
                SandboxToolPacket.Capture(action, cell, distanceFromOrigin, position));
        }
    }

    [HarmonyPatch(typeof(SandboxBrushTool), nameof(SandboxBrushTool.OnPaintCell))]
    internal static class SandboxBrushToolPatch
    {
        private static void Postfix(int cell, int distFromOrigin) =>
            SandboxToolSync.Send(SandboxToolAction.Brush, cell, distFromOrigin);
    }

    [HarmonyPatch(typeof(SandboxSprinkleTool), nameof(SandboxSprinkleTool.OnPaintCell))]
    internal static class SandboxSprinkleToolPatch
    {
        private static void Postfix(int cell, int distFromOrigin) =>
            SandboxToolSync.Send(SandboxToolAction.Sprinkle, cell, distFromOrigin);
    }

    [HarmonyPatch(typeof(SandboxFloodTool), nameof(SandboxFloodTool.PaintCell))]
    internal static class SandboxFloodToolPatch
    {
        private static void Postfix(int cell) => SandboxToolSync.Send(SandboxToolAction.Flood, cell);
    }

    [HarmonyPatch(typeof(SandboxSampleTool), nameof(SandboxSampleTool.Sample))]
    internal static class SandboxSampleToolPatch
    {
        private static void Postfix(int cell) => SandboxToolSync.Send(SandboxToolAction.Sample, cell);
    }

    [HarmonyPatch(typeof(SandboxHeatTool), nameof(SandboxHeatTool.OnPaintCell))]
    internal static class SandboxHeatToolPatch
    {
        private static void Postfix(int cell, int distFromOrigin) =>
            SandboxToolSync.Send(SandboxToolAction.Heat, cell, distFromOrigin);
    }

    [HarmonyPatch(typeof(SandboxStressTool), nameof(SandboxStressTool.OnPaintCell))]
    internal static class SandboxStressToolPatch
    {
        private static void Postfix(int cell, int distFromOrigin) =>
            SandboxToolSync.Send(SandboxToolAction.Stress, cell, distFromOrigin);
    }

    [HarmonyPatch(typeof(SandboxSpawnerTool), nameof(SandboxSpawnerTool.Place))]
    internal static class SandboxSpawnerToolPatch
    {
        private static void Postfix(int cell) => SandboxToolSync.Send(SandboxToolAction.Spawn, cell);
    }

    [HarmonyPatch(typeof(SandboxDestroyerTool), nameof(SandboxDestroyerTool.OnPaintCell))]
    internal static class SandboxDestroyerToolPatch
    {
        private static void Postfix(int cell, int distFromOrigin) =>
            SandboxToolSync.Send(SandboxToolAction.Destroy, cell, distFromOrigin);
    }

    [HarmonyPatch(typeof(SandboxFOWTool), nameof(SandboxFOWTool.OnPaintCell))]
    internal static class SandboxFowToolPatch
    {
        private static void Postfix(int cell, int distFromOrigin) =>
            SandboxToolSync.Send(SandboxToolAction.Reveal, cell, distFromOrigin);
    }

    [HarmonyPatch(typeof(SandboxClearFloorTool), nameof(SandboxClearFloorTool.OnPaintCell))]
    internal static class SandboxClearFloorToolPatch
    {
        private static void Postfix(int cell, int distFromOrigin) =>
            SandboxToolSync.Send(SandboxToolAction.ClearFloor, cell, distFromOrigin);
    }

    [HarmonyPatch(typeof(SandboxCritterTool), nameof(SandboxCritterTool.OnPaintCell))]
    internal static class SandboxCritterToolPatch
    {
        private static void Postfix(int cell, int distFromOrigin) =>
            SandboxToolSync.Send(SandboxToolAction.CritterRemoval, cell, distFromOrigin);
    }

    [HarmonyPatch(typeof(SandboxStoryTraitTool), nameof(SandboxStoryTraitTool.OnLeftClickDown))]
    internal static class SandboxStoryTraitToolPatch
    {
        private static void Prefix(SandboxStoryTraitTool __instance, Vector3 cursor_pos)
        {
            if (SandboxToolPacket.ProcessingIncoming || __instance == null || __instance.isPlacingTemplate)
                return;

            int cell = Grid.PosToCell(cursor_pos);
            if (Grid.IsValidCell(cell) && __instance.GetError(cursor_pos, out _, out _) == null)
                SandboxToolSync.Send(SandboxToolAction.StoryTrait, cell, position: cursor_pos);
        }
    }
}
