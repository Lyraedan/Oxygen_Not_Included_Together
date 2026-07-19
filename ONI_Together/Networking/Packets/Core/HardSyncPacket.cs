using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Core
{
	public class HardSyncPacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		public void Serialize(BinaryWriter writer)
		{
			// No payload needed
		}

		public void Deserialize(BinaryReader reader)
		{
			// No payload needed
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;

			// Hide all the player cursors on the client as they'll reappear as packets are recieved
			foreach (PlayerCursor cursor in MultiplayerSession.PlayerCursors.Values)
			{
				cursor.SetVisibility(false);
			}

			Sync();
			//PauseScreen.TriggerQuitGame();
		}

		public static void Sync()
		{
			using var _ = Profiler.Scope();

			ProductionDesyncRecovery.ReleaseClientForHardSync();
			GameClient.IsHardSyncInProgress = true;
			MultiplayerOverlay.Show(STRINGS.UI.MP_OVERLAY.SYNC.HARDSYNC_INPROGRESS);
			DebugConsole.Log("[HardSync] Client entering in-place sync, staying connected for save transfer");
		}
	}
}
