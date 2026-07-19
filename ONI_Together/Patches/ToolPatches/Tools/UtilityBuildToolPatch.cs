using System;
using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Tools.Build;
using Shared.Profiling;

namespace ONI_Together.Patches.ToolPatches.Build
{
	[HarmonyPatch(typeof(BaseUtilityBuildTool), nameof(BaseUtilityBuildTool.BuildPath))]
	public static class UtilityBuildToolPatch
	{
		static bool Prefix(BaseUtilityBuildTool __instance, out UtilityBuildCapture __state)
		{
			using var _ = Profiler.Scope();
			__state = null;
			bool runLocally = ShouldRunLocally(
				MultiplayerSession.InSession,
				MultiplayerSession.IsHost,
				UtilityBuildPacket.ProcessingIncoming);
			if (runLocally)
			{
				if (MultiplayerSession.IsHostInSession && !UtilityBuildPacket.ProcessingIncoming)
					__state = UtilityBuildAuthority.Capture(__instance);
				return true;
			}

			try
			{
				UtilityBuildCapture capture = UtilityBuildAuthority.Capture(__instance);
				if (capture?.Request == null)
					return false;
				PacketSender.SendToAllOtherPeers(capture.Request);
				DebugConsole.Log(
					$"[UtilityBuild] Requested {capture.Request.PrefabID} with {capture.Request.Cells.Count} nodes");
			}
			catch (Exception exception)
			{
				DebugConsole.LogError($"[UtilityBuildToolPatch.Prefix] {exception}");
			}
			return false;
		}

		static void Postfix(UtilityBuildCapture __state)
		{
			using var _ = Profiler.Scope();
			if (!MultiplayerSession.IsHostInSession || __state == null || UtilityBuildPacket.ProcessingIncoming)
				return;
			try
			{
				if (UtilityBuildAuthority.TryCaptureOutcome(__state, out UtilityBuildStatePacket state))
					PacketSender.SendToAllClients(state);
			}
			catch (Exception exception)
			{
				DebugConsole.LogError($"[UtilityBuildToolPatch.Postfix] {exception}");
			}
		}

		internal static bool ShouldRunLocally(bool inSession, bool isHost, bool processingIncoming)
			=> !inSession || isHost || processingIncoming;
	}
}
