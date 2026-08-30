using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Tools;
using ONI_Together.Networking.Packets.Tools.Build;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Shared.Profiling;

namespace ONI_Together.Patches.ToolPatches.Build
{
	// Try patching BuildPath - called when drag is complete and building is placed
	[HarmonyPatch(typeof(BaseUtilityBuildTool), nameof(BaseUtilityBuildTool.BuildPath))]
	public static class UtilityBuildToolPatch
	{
		public static void Prefix(BaseUtilityBuildTool __instance)
		{
			using var _ = Profiler.Scope();

			//DebugConsole.Log($"[UtilityBuildToolPatch] Prefix called! Tool type: {__instance.GetType().Name}");
			if (!MultiplayerSession.InActiveSession)
			{
				return;
			}
			//prevent recursion
			if (UtilityBuildPacket.ProcessingIncoming)
			{
				DebugConsole.Log("UtilityBuildPacket currently processing");
				return;
			}

			if (__instance.path == null || __instance.def == null || __instance.path.Count == 0)
			{
				DebugConsole.LogWarning("[UtilityBuildToolPatch] Path or Def is null, cannot send UtilityBuildPacket.");
				return;
			}

			bool instantBuild = DebugHandler.InstantBuildMode || (Game.Instance.SandboxModeActive && SandboxToolParameterMenu.instance.settings.InstantBuild);
			PacketSender.SendToAllOtherPeers(new UtilityBuildPacket(__instance.def.PrefabID, __instance.path, [.. __instance.selectedElements.Select(t => t.ToString())], __instance.facadeID, instantBuild));
			DebugConsole.Log($"[UtilityBuild] Sent packet for {__instance.def.PrefabID} with {__instance.path.Count} nodes.");
		}
	}
}
