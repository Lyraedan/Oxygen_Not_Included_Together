using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Misc.World;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Transfer;
using ONI_Together.Networking.Transport.External;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	public class TcpTransferStartPacket : IPacket, IPriorityPacket, IAllowedWithoutWorldPacket
	{
		public int TcpPort;
		public string FileName;
		public int FileSize;
		public ulong ClientId;
		public string Token;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(TcpPort);
			writer.Write(FileName);
			writer.Write(FileSize);
			writer.Write(ClientId);
			writer.Write(Token ?? string.Empty);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			TcpPort = reader.ReadInt32();
			FileName = reader.ReadString();
			FileSize = reader.ReadInt32();
			ClientId = reader.ReadUInt64();
			Token = reader.ReadString();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;

			string hostIp = Configuration.Instance.Client.LanSettings.Ip;

			// Direct custom transports know the host address the client actually
			// joined with, which is more reliable than the LAN setting.
			if (NetworkConfig.TransportClient is ExternalTransportClient externalClient && externalClient.SupportsFileTransfer)
			{
				string providedHost = externalClient.GetFileTransferHost();
				if (!string.IsNullOrWhiteSpace(providedHost))
					hostIp = providedHost;
			}

			DebugConsole.Log($"[TcpTransferStart] Starting TCP download from {hostIp}:{TcpPort}, file '{FileName}' ({FileSize} bytes), clientId={ClientId}");

			MultiplayerOverlay.Show("Connecting for save download...");

			TcpFileTransferClient.Download(hostIp, TcpPort, ClientId, Token,
				(fileName, data) =>
				{
					DebugConsole.Log($"[TcpTransferStart] TCP download complete: '{fileName}' ({data.Length} bytes)");
					var worldSave = new WorldSave(fileName, data);
					SaveHelper.RequestWorldLoad(worldSave);
				},
				(error) =>
				{
					DebugConsole.LogWarning($"[TcpTransferStart] TCP download failed: {error}. Requesting UDP fallback.");
					MultiplayerOverlay.Show("TCP connection failed. Downloading save via UDP...");

					var fallback = new TcpFallbackRequestPacket { Requester = ClientId };
					PacketSender.SendToHost(fallback);
				});
		}
	}
}
