using HarmonyLib;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.World;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.World
{
	/// <summary>
	/// Patches for ISidescreenButtonControl button presses.
	/// Note: Most button controls require patching specific implementations.
	/// Add patches as needed when specific button methods are identified.
	/// </summary>

	// TODO: Add specific button patches as needed
	// Common examples that need investigation:
	// - Door state control (Open/Close/Auto)
	// - Toilet flush button
	// - Limit valve settings
	// - Access Control permissions

	// Placeholder class with helper method
	public static class SidescreenButtonPatches
	{
		// Helper method for sending button press changes
		public static void SyncButtonPress(UnityEngine.Component component, string configId, float value)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;
			if (component == null) return;

			var identity = component.GetComponent<NetworkIdentity>();
			if (identity == null) return;

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				ConfigHash = configId.GetHashCode(),
				Value = value,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost)
				PacketSender.SendToAllClients(packet);
			else
				PacketSender.SendToHost(packet);
		}
	}
}
