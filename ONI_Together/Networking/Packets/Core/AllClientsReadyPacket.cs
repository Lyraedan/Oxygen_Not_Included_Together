using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Networking.Packets.Architecture;
using System.Collections;
using System.IO;
using Shared.Profiling;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Core
{
	public class AllClientsReadyPacket : IPacket, IHostOnlyPacket
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
            //SpeedControlScreen.Instance?.Unpause(false);
		}

		private static IEnumerator CloseOverlayAfterDelay()
		{
			using var _ = Profiler.Scope();

			MultiplayerOverlay.Show(STRINGS.UI.MP_OVERLAY.SYNC.FINALIZING_SYNC);
			yield return new WaitForSecondsRealtime(1f);
            MultiplayerOverlay.Close();
            //SpeedControlScreen.Instance?.Unpause(false);
		}
	}
}
