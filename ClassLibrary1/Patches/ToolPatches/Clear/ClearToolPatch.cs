using HarmonyLib;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Tools.Clear;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.ToolPatches.Clear
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
