using HarmonyLib;
using ONI_Together.Misc.World;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
	[HarmonyPatch(typeof(SimMessages), nameof(SimMessages.ModifyCell))]
	public static class SimMessagesPatch
	{
		[HarmonyPrefix]
		public static void Prefix(
				int gameCell,
				ushort elementIdx,
				float temperature,
				float mass,
				byte disease_idx,
				int disease_count,
				SimMessages.ReplaceType replace_type,
				bool do_vertical_solid_displacement,
				int callbackIdx
		)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost || !Grid.IsValidCell(gameCell)) return;

			// Enqueue update to batcher
			WorldUpdateBatcher.Queue(new WorldUpdatePacket.CellUpdate
			{
				Cell = gameCell,
				ElementIdx = elementIdx,
				Temperature = temperature,
				Mass = mass,
				DiseaseIdx = disease_idx,
				DiseaseCount = disease_count
			});
		}
	}
}
