using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
	[HarmonyPatch(typeof(ResearchEntry), "OnResearchClicked")]
	public static class ResearchEntryPatch
	{
		public static bool Prefix(ResearchEntry __instance)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession) return true; // Offline, operate normally
			if (MultiplayerSession.IsHost) return true; // Host operates normally

			// Client: Send Request
			var targetTech = __instance.targetTech;
			if (targetTech != null)
			{
				var packet = new ResearchRequestPacket
				{
					TechId = targetTech.Id
				};
				PacketSender.SendToHost(packet);
				ONI_Together.DebugTools.DebugConsole.Log($"[Client] Requested research: {targetTech.Id}");
			}

			// Suppress local sound/state change until confirmed?
			// Existing method plays sound and calls Research.Instance.SetActiveResearch.
			// If we assume latency, we might want to let it run locally if we trust it,
			// OR suppress and wait for ResearchStatePacket to confirm active research.
			// Let's suppress to prevent desync (e.g. invalid research).

			return false;
		}
	}
}
