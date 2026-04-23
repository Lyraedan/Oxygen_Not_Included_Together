using ONI_MP.Menus;
using ONI_MP.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using ONI_MP.Networking;

namespace ONI_MP.Networking.Packets.Core
{
	public class HardSyncCompletePacket : IPacket
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

			SpeedControlScreen.Instance?.Unpause(false);
			MultiplayerOverlay.Close();
		}

	}
}
