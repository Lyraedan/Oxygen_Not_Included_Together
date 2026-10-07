using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Core
{
	public class HardSyncPacket : IPacket, IAllowedWithoutWorldPacket
	{
		public HardSyncPacket() { }

		public void Serialize(BinaryWriter writer) { }

		public void Deserialize(BinaryReader reader) { }

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;

			if (!GameClient.BeginSynchronization())
				return;

			// Hide cursors until their current positions arrive after synchronization.
			foreach (PlayerCursor cursor in MultiplayerSession.PlayerCursors.Values)
			{
				cursor.SetVisibility(false);
			}
			//PauseScreen.TriggerQuitGame();
		}

	}
}
