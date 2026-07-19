using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Misc.World;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Transfer;
using Shared.Profiling;
using Shared.Interfaces.Networking;
using System;
using System.Threading;

namespace ONI_Together.Networking.Packets.World
{
	public class TcpTransferStartPacket : IPacket, IHostOnlyPacket
	{
		internal readonly struct TransferBinding
		{
			public readonly long Generation;
			public readonly ulong HostId;
			public readonly ulong ClientId;

			public TransferBinding(long generation, ulong hostId, ulong clientId)
			{
				Generation = generation;
				HostId = hostId;
				ClientId = clientId;
			}
		}

		private static long _transferGeneration;
		private static CancellationTokenSource _activeDownload;
		public int TcpPort;
		public string FileName;
		public int FileSize;
		public ulong ClientId;
		public long SnapshotGeneration;
		public string TransferToken;
		public byte[] FileHash;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(TcpPort);
			writer.Write(FileName);
			writer.Write(FileSize);
			writer.Write(ClientId);
			writer.Write(SnapshotGeneration);
			writer.Write(TransferToken ?? string.Empty);
			writer.Write(FileHash?.Length ?? 0);
			if (FileHash != null)
				writer.Write(FileHash);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			TcpPort = reader.ReadInt32();
			FileName = reader.ReadString();
			FileSize = reader.ReadInt32();
			ClientId = reader.ReadUInt64();
			SnapshotGeneration = reader.ReadInt64();
			TransferToken = reader.ReadString();
			if (TransferToken.Length == 0 || TransferToken.Length > SecureTransferPacket.MaxTransferIdChars)
				throw new InvalidDataException("Invalid TCP transfer token");
			int hashLength = reader.ReadInt32();
			if (hashLength != 32)
				throw new InvalidDataException("Invalid TCP save hash length");
			FileHash = reader.ReadBytes(hashLength);
			if (FileHash.Length != hashLength)
				throw new EndOfStreamException("TCP save hash is truncated");
			if (TcpPort <= 0 || TcpPort > 65535 || SnapshotGeneration <= 0 || FileName.Length == 0
			    || FileName.Length > SaveFileChunkPacket.MaxFileNameChars
			    || FileSize <= 0 || FileSize > SaveFileChunkPacket.MaxSaveBytes)
				throw new InvalidDataException("Invalid TCP transfer metadata");
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost || !PacketHandler.CurrentContext.SenderIsHost
			    || ClientId != MultiplayerSession.LocalUserID)
				return;
			if (!SaveChunkAssembler.BeginSnapshot(SnapshotGeneration))
			{
				DebugConsole.LogWarning($"[TcpTransferStart] Rejected stale snapshot generation {SnapshotGeneration}");
				return;
			}

			string hostIp = Configuration.Instance.Client.LanSettings.Ip;

			DebugConsole.Log($"[TcpTransferStart] Starting TCP download from {hostIp}:{TcpPort}, file '{FileName}' ({FileSize} bytes), clientId={ClientId}");

			MultiplayerOverlay.Show("Connecting for save download...");
			_activeDownload?.Cancel();
			_activeDownload?.Dispose();
			long generation = Interlocked.Increment(ref _transferGeneration);
			ulong expectedHost = PacketHandler.CurrentContext.SenderId;
			ulong expectedClient = ClientId;
			string expectedToken = TransferToken;
			_activeDownload = TcpFileTransferClient.Download(
				hostIp, TcpPort, ClientId, TransferToken, FileName, FileSize, FileHash,
				(fileName, data) =>
				{
					if (!IsCurrentCallback(generation, expectedHost, expectedClient))
						return;
					FinishDownload();
					DebugConsole.Log($"[TcpTransferStart] TCP download complete: '{fileName}' ({data.Length} bytes)");
					var worldSave = new WorldSave(fileName, data, SnapshotGeneration);
					SaveHelper.RequestWorldLoad(worldSave);
				},
				(error) =>
				{
					if (!IsCurrentCallback(generation, expectedHost, expectedClient))
						return;
					FinishDownload();
					DebugConsole.LogWarning($"[TcpTransferStart] TCP download failed: {error}. Requesting UDP fallback.");
					MultiplayerOverlay.Show("TCP connection failed. Downloading save via UDP...");

					var fallback = new TcpFallbackRequestPacket
					{
						Requester = expectedClient,
						TransferToken = expectedToken
					};
					PacketSender.SendToHost(fallback);
				});
		}

		private static bool IsCurrentCallback(long generation, ulong expectedHost, ulong expectedClient)
		{
			return IsCurrentTransfer(
				new TransferBinding(generation, expectedHost, expectedClient),
				new TransferBinding(_transferGeneration, MultiplayerSession.HostUserID,
					MultiplayerSession.LocalUserID),
				MultiplayerSession.InSession && MultiplayerSession.IsClient);
		}

		internal static bool IsCurrentTransfer(
			TransferBinding expected,
			TransferBinding current,
			bool isActiveClient)
		{
			return isActiveClient && expected.Generation == current.Generation
			    && expected.HostId == current.HostId && expected.ClientId == current.ClientId;
		}

		private static void FinishDownload()
		{
			_activeDownload?.Dispose();
			_activeDownload = null;
		}

		internal static void CancelActiveDownload()
		{
			Interlocked.Increment(ref _transferGeneration);
			_activeDownload?.Cancel();
			FinishDownload();
		}
	}
}
