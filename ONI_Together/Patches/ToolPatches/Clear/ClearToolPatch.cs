using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Tools.Clear;
using Shared.Profiling;

namespace ONI_Together.Patches.ToolPatches.Clear
{
	[HarmonyPatch(typeof(ClearTool), "OnDragTool")]
	public static class ClearTool_OnDragTool_Patch
	{
		public static void Prefix(int cell, int distFromOrigin)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession)
				return;

			if (!Grid.IsValidCell(cell))
				return;

			if (ClearPacket.ProcessingIncoming)
				return;

			PacketSender.SendToAllOtherPeers(new ClearPacket { cell = cell, distFromOrigin = distFromOrigin });
		}
	}

}
