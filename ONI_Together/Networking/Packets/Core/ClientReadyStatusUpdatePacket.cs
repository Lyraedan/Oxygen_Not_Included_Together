using ONI_Together.Menus;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Core
{
	public class ClientReadyStatusUpdatePacket : IPacket, IAllowedWithoutWorldPacket
	{
		public string Message;

		public ClientReadyStatusUpdatePacket() { }

		public ClientReadyStatusUpdatePacket(string message)
		{
			using var _ = Profiler.Scope();

			Message = message;
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(Message);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Message = reader.ReadString();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			// Host updates theirs on each ready status packet so we dont do anything here
			if (MultiplayerSession.IsHost)
				return;

			MultiplayerOverlay.Show(Message);
		}
	}
}
