using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Core
{
	public class AllClientsReadyPacket : IPacket, IAllowedWithoutWorldPacket
	{
		public AllClientsReadyPacket() { }

		public void Serialize(BinaryWriter writer) { }

		public void Deserialize(BinaryReader reader) { }

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost || !GameClient.CompleteSynchronization())
				return;

			DebugConsole.Log("[AllClientsReadyPacket] Synchronization completed; closing overlay");
			ProcessAllReady();
		}

		public static void ProcessAllReady()
		{
			using var _ = Profiler.Scope();

			MultiplayerOverlay.Show(STRINGS.UI.MP_OVERLAY.SYNC.FINALIZING_SYNC);
            MultiplayerOverlay.Close();
            //SpeedControlScreen.Instance?.Unpause(false);
		}

	}
}
