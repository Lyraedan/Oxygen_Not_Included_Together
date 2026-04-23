using HarmonyLib;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.World;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.World
{
	[HarmonyPatch(typeof(FallingWater), "AddParticle", new System.Type[] { typeof(int), typeof(ushort), typeof(float), typeof(float), typeof(byte), typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
	public static class FallingWaterPatch
	{
		// Intercept creation of falling particles
		// Signature: (int cell, ushort elementIdx, float mass, float temperature, byte diseaseIdx, int diseaseCount, bool skip_sound, bool skip_decorate, bool debug_track, bool is_canister)
		// We might need to match arguments precisely.

		public static bool Prefix(
				int cell,
				ushort elementIdx,
				float base_mass,
				float temperature,
				byte disease_idx,
				int base_disease_count,
				bool skip_sound,
				bool skip_decor,
				bool debug_track,
				bool disable_randomness)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession) return true;

			if (FallingObjectPacket.IsApplying) return true; // Allow packet application

			if (MultiplayerSession.IsHost)
			{
				// Host: Allow, and Send Packet
				// Only sync if significant mass?
				if (base_mass > 0.1f)
				{
					var packet = new FallingObjectPacket
					{
						Cell = cell,
						ElementIndex = elementIdx,
						Mass = base_mass,
						Temperature = temperature,
						DiseaseIndex = disease_idx,
						DiseaseCount = base_disease_count,
						IsLiquid = ElementLoader.elements[elementIdx].IsLiquid
					};
					PacketSender.SendToAllClients(packet);
				}
				return true;
			}
			else
			{
				// Client: Suppress local creation to avoid desync/duplication
				// Unless it's confirmed that Client Sim doesn't trigger this?
				// If we suppress, we rely 100% on packet.
				// This essentially disables client-side falling physics initiation.
				return false;
			}
		}
	}
}
