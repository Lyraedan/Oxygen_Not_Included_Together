using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Tools;
using ONI_Together.Networking.Packets.Tools.Prioritize;
using System.Collections.Generic;
using Shared.Profiling;
using UnityEngine;

[HarmonyPatch(typeof(PrioritizeTool), nameof(PrioritizeTool.OnDragTool))]
public static class PrioritizeToolPatch
{
	public static void Postfix(int cell, int distFromOrigin)
	{
		using var _ = Profiler.Scope();

		if (!MultiplayerSession.InActiveSession)
			return;

		//prevent recursion
		if (PrioritizePacket.ProcessingIncoming)
			return;

		PacketSender.SendToAllOtherPeers(new PrioritizePacket { cell = cell, distFromOrigin = distFromOrigin });
	}
}
