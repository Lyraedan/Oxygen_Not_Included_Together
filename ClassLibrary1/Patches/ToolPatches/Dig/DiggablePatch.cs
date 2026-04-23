using HarmonyLib;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Tools.Dig;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.ToolPatches.Dig
{
	[HarmonyPatch(typeof(Diggable), "OnStopWork")]
	public static class DiggablePatch
	{
		public static void Prefix(Diggable __instance)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost || !MultiplayerSession.InSession)
				return;

			int cell = __instance.GetCell();

			if (!Grid.IsValidCell(cell))
				return;

			var packet = new DigCompletePacket
			{
				Cell = cell,
				Mass = Grid.Mass[cell],
				Temperature = Grid.Temperature[cell],
				ElementIdx = Grid.ElementIdx[cell],
				DiseaseIdx = Grid.DiseaseIdx[cell],
				DiseaseCount = Grid.DiseaseCount[cell]
			};

			PacketSender.SendToAllClients(packet);
		}
	}
}
