using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	public class TcpFallbackRequestPacket : IPacket
	{
		public ulong Requester;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(Requester);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Requester = reader.ReadUInt64();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;

			DebugConsole.Log($"[TcpFallback] Client {Requester} requested UDP fallback for save transfer");
			SaveFileRequestPacket.SendSaveFileViaUdp(Requester);
		}
	}
}
