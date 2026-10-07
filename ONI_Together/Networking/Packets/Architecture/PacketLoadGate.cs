using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.Handshake;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.Networking.Packets.Architecture
{
	internal static class PacketLoadGate
	{
		public static bool Allows(IPacket packet)
		{
			return packet is HardSyncPacket
				|| packet is ClientReadyStatusPacket
				|| packet is ClientReadyStatusUpdatePacket
				|| packet is AllClientsReadyPacket
				|| packet is SaveFileRequestPacket
				|| packet is SaveFileChunkPacket
				|| packet is SecureTransferPacket
				|| packet is ChunkedPacket
				|| packet is ChunkAckPacket
				|| packet is TcpTransferStartPacket
				|| packet is TcpFallbackRequestPacket
				|| packet is SyncProgressPacket
				|| packet is GameStateRequestPacket;
		}
	}
}
