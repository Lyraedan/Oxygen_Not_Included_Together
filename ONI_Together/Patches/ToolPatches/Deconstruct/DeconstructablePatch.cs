using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Tools.Deconstruct;
using Shared.Profiling;

namespace ONI_Together.Patches.ToolPatches.Deconstruct
{
	[HarmonyPatch(typeof(Deconstructable), "OnCompleteWork")]
	public static class DeconstructablePatch
	{
		public static void Prefix(Deconstructable __instance)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost || !MultiplayerSession.InSession)
				return;

			int cell = __instance.NaturalBuildingCell();
			var packet = new DeconstructCompletePacket { Cell = cell, ObjectLayer = (int)__instance.GetComponent<Building>().Def.ObjectLayer };
			PacketSender.SendToAllClients(packet);

			DebugConsole.Log($"[DeconstructComplete] Host sent DeconstructCompletePacket for cell {cell}");
		}
	}
}
