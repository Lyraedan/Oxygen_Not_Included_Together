using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Tools;
using ONI_Together.Networking.Packets.Tools.Cancel;
using Shared.Profiling;

namespace ONI_Together.Patches.ToolPatches.Cancel
{
	[HarmonyPatch(typeof(CancelTool), nameof(CancelTool.OnDragTool))]
	public static class CancelToolPatch
	{
		public static void Postfix(int cell, int distFromOrigin)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession)
				return;

			//prevent recursion
			if (CancelPacket.ProcessingIncoming)
				return;
			PacketSender.SendToAllOtherPeers(new CancelPacket() { cell = cell, distFromOrigin = distFromOrigin });
		}
	}
}
