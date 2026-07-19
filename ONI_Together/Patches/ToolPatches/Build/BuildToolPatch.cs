using System;
using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Tools.Build;
using Shared.Profiling;

namespace ONI_Together.Patches.ToolPatches.Build
{
	[HarmonyPatch(typeof(BuildTool), nameof(BuildTool.TryBuild))]
	public static class BuildToolPatch
	{
		static bool Prefix(BuildTool __instance, int cell, out BuildCapture __state)
		{
			using var _ = Profiler.Scope();
			__state = null;
			if (ShouldRunLocally(MultiplayerSession.InSession, MultiplayerSession.IsHost, false))
			{
				if (MultiplayerSession.InSession && MultiplayerSession.IsHost)
					__state = BuildAuthority.Capture(__instance, cell);
				return true;
			}

			try
			{
				if (!CanIssueClientRequest(__instance, cell))
					return false;
				BuildCapture capture = BuildAuthority.Capture(__instance, cell);
				if (capture?.Request == null)
					return false;
				AdvanceClientToolState(__instance, cell);
				PacketSender.SendToAllOtherPeers(capture.Request);
				DebugConsole.Log($"[BuildTool] Requested {capture.Request.PrefabID} at cell {cell}");
			}
			catch (Exception exception)
			{
				DebugConsole.LogError($"[BuildToolPatch.Prefix] {exception}");
			}
			return false;
		}

		static void Postfix(BuildCapture __state)
		{
			using var _ = Profiler.Scope();
			if (!MultiplayerSession.IsHostInSession || __state == null)
				return;
			try
			{
				if (BuildAuthority.TryCaptureOutcome(__state, out BuildStatePacket state))
					PacketSender.SendToAllClients(state);
			}
			catch (Exception exception)
			{
				DebugConsole.LogError($"[BuildToolPatch.Postfix] {exception}");
			}
		}

		internal static bool ShouldRunLocally(bool inSession, bool isHost, bool processingIncoming)
			=> !inSession || isHost || processingIncoming;

		private static bool CanIssueClientRequest(BuildTool tool, int cell)
		{
			if (tool?.def == null || tool.visualizer == null || tool.selectedElements == null ||
			    !Grid.IsValidCell(cell) || !Grid.IsVisible(cell))
				return false;
			if (cell == tool.lastDragCell && tool.buildingOrientation == tool.lastDragOrientation)
				return false;
			bool positionBound = tool.def.BuildingComplete.GetComponent<LogicPorts>() != null ||
			                     tool.def.BuildingComplete.GetComponent<LogicGateBase>() != null;
			return !positionBound || Grid.PosToCell(tool.visualizer) == cell;
		}

		private static void AdvanceClientToolState(BuildTool tool, int cell)
		{
			tool.lastDragCell = cell;
			tool.lastDragOrientation = tool.buildingOrientation;
			tool.ClearTilePreview();
			if (PlanScreen.Instance != null)
				PlanScreen.Instance.LastSelectedBuildingFacade = tool.facadeID;
		}
	}
}
