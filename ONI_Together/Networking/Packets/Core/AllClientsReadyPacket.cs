using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Core
{
	public class AllClientsReadyPacket : IPacket, IAllowedWithoutWorldPacket
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
		}

	}
}
