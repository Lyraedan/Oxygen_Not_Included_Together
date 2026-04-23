using ONI_MP.DebugTools;
using ONI_MP.Networking.Packets.Architecture;
using Steamworks;
using System.IO;
using Shared.Profiling;
using ONI_MP.Networking;
using Utils = ONI_MP.Misc.Utils;

namespace ONI_MP.Networking.Packets.World
{
	public class WorldDataRequestPacket : IPacket
	{
		public ulong SenderId;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(SenderId);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			SenderId = reader.ReadUInt64();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;

			// Immediately send full world data back to the requester
			SendWorldData(SenderId);
		}

		private void SendWorldData(ulong target)
		{
			using var _ = Profiler.Scope();

			DebugConsole.Log($"[WorldDataRequestPacket] Sending world data to {target}");

			var chunks = Utils.CollectChunks(
					startX: 0,
					startY: 0,
					chunkSize: 32,
					numChunksX: Grid.WidthInCells / 32,
					numChunksY: Grid.HeightInCells / 32
			);

			var packet = new WorldDataPacket { Chunks = chunks };
			PacketSender.SendToPlayer(target, packet);

			DebugConsole.Log($"[WorldDataRequestPacket] WorldDataPacket sent to {target}");
		}
	}
}
