using HarmonyLib;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.World;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.World
{
	/// <summary>
	/// Patch to detect when research completes and sync to all clients.
	/// Patches TechInstance.Purchased which is called when a tech is completed.
	/// </summary>
	[HarmonyPatch(typeof(TechInstance), "Purchased")]
	public static class ResearchCompletePatch
	{
		public static void Postfix(TechInstance __instance)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost) return;
			if (__instance?.tech == null) return;

			// Prevent sending completion if we're applying state from a received packet
			if (ResearchStatePacket.IsApplying) return;

			// Send completion packet to all clients
			var packet = new ResearchCompletePacket
			{
				TechId = __instance.tech.Id
			};

			PacketSender.SendToAllClients(packet);
			ONI_MP.DebugTools.DebugConsole.Log($"[ResearchCompletePatch] Sent completion for: {__instance.tech.Name}");
		}
	}
}

