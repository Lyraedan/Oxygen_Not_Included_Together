using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Tools.Cancel;
using ONI_Together.Networking.Packets.Tools.Deconstruct;
using Shared.Profiling;

namespace ONI_Together.Patches.ToolPatches.Deconstruct
{
	[HarmonyPatch(typeof(DeconstructTool), nameof(DeconstructTool.OnDragTool))]
	public static class DeconstructToolPatch
	{
		public static void Postfix(int cell, int distFromOrigin)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession)
				return;

			//prevent recursion
			if (DeconstructPacket.ProcessingIncoming)
				return;
			PacketSender.SendToAllOtherPeers(new DeconstructPacket() { cell = cell, distFromOrigin = distFromOrigin });
		}
	}
}
