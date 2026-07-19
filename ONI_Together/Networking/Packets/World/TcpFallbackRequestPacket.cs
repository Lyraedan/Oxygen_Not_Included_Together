using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	public class TcpFallbackRequestPacket : IPacket
	{
		private static int _rejectedPackets;
		public ulong Requester;
		public string TransferToken = string.Empty;
		internal static bool ShouldAccept(ulong requester, DispatchContext context) =>
			requester != 0 && !context.SenderIsHost && SyncBarrier.SenderMatches(requester, context.SenderId);

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(Requester);
			writer.Write(TransferToken ?? string.Empty);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Requester = reader.ReadUInt64();
			TransferToken = reader.ReadString();
			if (TransferToken.Length == 0
			    || TransferToken.Length > SecureTransferPacket.MaxTransferIdChars)
				throw new InvalidDataException("Invalid TCP fallback token");
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;
			if (!ShouldAccept(Requester, PacketHandler.CurrentContext))
			{
				int rejected = ++_rejectedPackets;
				if (rejected <= 5 || rejected % 100 == 0)
					DebugConsole.LogWarning($"[TcpFallback] Rejected requester {Requester} from {PacketHandler.CurrentContext.SenderId}, host={PacketHandler.CurrentContext.SenderIsHost} (#{rejected})");
				return;
			}
			MultiplayerPlayer player = MultiplayerSession.GetPlayer(Requester);
			if (player == null || !player.TryRequestSaveFallback(TransferToken))
			{
				DebugConsole.LogWarning($"[TcpFallback] Rejected duplicate or stale transfer from {Requester}");
				return;
			}

			DebugConsole.Log($"[TcpFallback] Client {Requester} requested UDP fallback for save transfer");
			if (NetworkConfig.TransportServer is ONI_Together.Networking.Transport.Lan.RiptideServer server)
				server.TcpTransfer?.CancelTransfer(Requester, TransferToken);
			SaveFileRequestPacket.SendSaveFileViaUdp(Requester);
		}
	}
}
