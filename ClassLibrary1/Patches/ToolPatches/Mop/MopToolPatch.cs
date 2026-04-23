using HarmonyLib;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Tools.Mop;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.ToolPatches.Mop
{
	[HarmonyPatch(typeof(MopTool), "OnDragTool")]
	public static class MoptoolPatch
	{
		public static void Prefix(int cell, int distFromOrigin)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession)
				return;

			if (!Grid.IsValidCell(cell))
				return;

			if (MopToolPacket.ProcessingIncoming)
				return;

			PacketSender.SendToAllOtherPeers(new MopToolPacket { cell = cell, distFromOrigin = distFromOrigin });
		}
	}

}
