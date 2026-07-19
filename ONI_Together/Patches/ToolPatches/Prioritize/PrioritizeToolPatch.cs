using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Tools.Prioritize;
using Shared.Profiling;

[HarmonyPatch(typeof(PrioritizeTool), nameof(PrioritizeTool.OnDragTool))]
public static class PrioritizeToolPatch
{
	public static bool Prefix(int cell, int distFromOrigin)
	{
		using var _ = Profiler.Scope();

		if (ShouldRunLocally(
			    MultiplayerSession.InSession,
			    MultiplayerSession.IsHost,
			    PrioritizePacket.ProcessingIncoming))
			return true;

		PacketSender.SendToAllOtherPeers(new PrioritizePacket { cell = cell, distFromOrigin = distFromOrigin });
		return false;
	}

	internal static bool ShouldRunLocally(bool inSession, bool isHost, bool processingIncoming)
		=> !inSession || isHost || processingIncoming;
}
