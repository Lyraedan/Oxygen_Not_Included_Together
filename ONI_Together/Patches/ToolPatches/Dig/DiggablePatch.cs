using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using Shared.Profiling;

namespace ONI_Together.Patches.ToolPatches.Dig
{
	[HarmonyPatch(typeof(Diggable), "OnStopWork")]
	public static class DiggablePatch
	{
		public static void Prefix(Diggable __instance)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost || !MultiplayerSession.InActiveSession)
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
