using HarmonyLib;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Tools.Deconstruct;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.ToolPatches.Deconstruct
{
	[HarmonyPatch(typeof(DeconstructTool), nameof(DeconstructTool.OnDragTool))]
	public static class DeconstructToolPatch
	{
		public static void Postfix(int cell, int distFromOrigin)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession)
				return;

			//prevent recursion
			if (DeconstructPacket.ProcessingIncoming)
				return;
			PacketSender.SendToAllOtherPeers(new DeconstructPacket() { cell = cell, distFromOrigin = distFromOrigin });
		}
	}
}
