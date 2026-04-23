using System.IO;
using ONI_MP.DebugTools;
using ONI_MP.Menus;
using ONI_MP.Misc.World;
using ONI_MP.Networking.Packets.Architecture;
using ONI_MP.Networking.Transfer;
using Shared.Profiling;
using ONI_MP.Networking;

namespace ONI_MP.Networking.Packets.World
{
	public class TcpTransferStartPacket : IPacket
	{
		public int TcpPort;
		public string FileName;
		public int FileSize;
		public ulong ClientId;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(TcpPort);
			writer.Write(FileName);
			writer.Write(FileSize);
			writer.Write(ClientId);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			TcpPort = reader.ReadInt32();
			FileName = reader.ReadString();
			FileSize = reader.ReadInt32();
			ClientId = reader.ReadUInt64();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;

			string hostIp = Configuration.Instance.Client.LanSettings.Ip;

			DebugConsole.Log($"[TcpTransferStart] Starting TCP download from {hostIp}:{TcpPort}, file '{FileName}' ({FileSize} bytes), clientId={ClientId}");

			MultiplayerOverlay.Show("Connecting for save download...");

			TcpFileTransferClient.Download(hostIp, TcpPort, ClientId,
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
