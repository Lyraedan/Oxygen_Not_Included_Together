using ONI_MP.DebugTools;
using ONI_MP.Menus;
using ONI_MP.Networking.Packets.Architecture;
using System.Collections;
using System.IO;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Networking.Packets.Core
{
	public class AllClientsReadyPacket : IPacket
	{

		public void Serialize(BinaryWriter writer)
		{
			// No payload needed for now
		}

		public void Deserialize(BinaryReader reader)
		{
			// No payload to read
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			DebugConsole.Log("[AllClientsReadyPacket] All players are ready! Closing overlay");
			ProcessAllReady();
		}

		public static void ProcessAllReady()
		{
			using var _ = Profiler.Scope();

			//CoroutineRunner.RunOne(CloseOverlayAfterDelay());
			MultiplayerOverlay.Show(STRINGS.UI.MP_OVERLAY.SYNC.FINALIZING_SYNC);
            MultiplayerOverlay.Close();
            SpeedControlScreen.Instance?.Unpause(false);
		}

	}
}
