using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Tools.Build;
using System;
using System.Collections.Generic;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.ToolPatches.Build
{
    [HarmonyPatch(typeof(BuildTool), nameof(BuildTool.TryBuild))]
    public static class BuildToolPatch
    {
        static void Prefix(BuildTool __instance, int cell)
        {
            using var _ = Profiler.Scope();

            try
            {
                var def = __instance.def;
                if (def != null)
                {
                    DebugConsole.Log($"[BuildTool] Attempting to build: {def.PrefabID} at cell {cell}");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[BuildToolPatch.Prefix] {ex}");
            }
        }

        static void Postfix(BuildTool __instance, int cell)
        {
            using var _ = Profiler.Scope();

            try
            {
                if (!MultiplayerSession.InSession || __instance == null)
                    return;

                var def = __instance.def;
                var selectedElements = __instance.selectedElements;
                var orientation = __instance.GetBuildingOrientation;

                if (def == null || selectedElements == null)
                    return;

                GameObject obj = Grid.Objects[cell, (int) def.ObjectLayer];
                if (obj != null)
                {
                    DebugConsole.Log($"[BuildTool] Successfully placed {def.PrefabID} at cell {cell}");
                }
                else
                {
                    // It might be a ghost/preview, so we still send the packet!
                    DebugConsole.Log($"[BuildTool] Placed intention/ghost for {def.PrefabID} at cell {cell}");
                }

                // Create and send packet
                var packet = new BuildPacket(
                    def.PrefabID,
                    cell,
                    orientation,
                    selectedElements,
                    def.ObjectLayer
                );

                PacketSender.SendToAllOtherPeers(packet);
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[BuildToolPatch.Postfix] {ex}");
            }
        }
    }
}