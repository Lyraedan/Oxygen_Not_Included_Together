using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Transport.Lan;
using ONI_Together.Networking.Transport.Steamworks;
using Steamworks;
using System;
using System.Collections;
using System.IO;
using Shared.Profiling;
using ONI_Together.Menus;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace ONI_Together.Networking.Packets.World
{
	public class SaveFileRequestPacket : IPacket
	{
		public ulong Requester;
		public string RestartTransferId = string.Empty;

		internal static SaveFileRequestPacket CreateRestart(
			ulong requester, string transferId)
		{
			if (requester == 0 || string.IsNullOrEmpty(transferId)
			    || transferId.Length > SecureTransferPacket.MaxTransferIdChars)
			{
				throw new ArgumentException("Invalid save transfer restart identity");
			}
			return new SaveFileRequestPacket
			{
				Requester = requester,
				RestartTransferId = transferId
			};
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(Requester);
			writer.Write(RestartTransferId ?? string.Empty);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Requester = reader.ReadUInt64();
			RestartTransferId = reader.ReadString();
			if (RestartTransferId.Length > SecureTransferPacket.MaxTransferIdChars)
				throw new InvalidDataException("Invalid save restart transfer id");
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;
			if (!SyncBarrier.SenderMatches(Requester, PacketHandler.CurrentContext.SenderId))
			{
				DebugConsole.LogWarning($"[Packets/SaveFileRequest] Rejected spoofed requester {Requester} from {PacketHandler.CurrentContext.SenderId}");
				return;
			}
			if (!ReadyManager.BeginSyncBarrier(Requester))
			{
				DebugConsole.LogWarning($"[Packets/SaveFileRequest] Rejected request from unknown or unverified player {Requester}");
				return;
			}

			DebugConsole.Log($"[Packets/SaveFileRequest] Received request from {Requester}");
			MultiplayerOverlay.Show(STRINGS.UI.MP_OVERLAY.HOST.SEND_SAVE_FILE);
			SendSaveFile(Requester, RestartTransferId);
		}

		public static void SendSaveFile(ulong requester)
			=> SendSaveFile(requester, string.Empty);

		private static void SendSaveFile(ulong requester, string restartTransferId)
		{
			using var _ = Profiler.Scope();
			if (!MultiplayerSession.IsHost)
				return;

			if (!TryBeginTransfer(requester, restartTransferId, out long transferGeneration))
			{
				DebugConsole.LogWarning($"[SaveFileRequest] Rejected stale or duplicate transfer for {requester}");
				return;
			}
			StartSnapshotTransfer(requester, preferTcp: true, transferGeneration);
		}

		public static void SendSaveFileViaUdp(ulong requester)
		{
			using var _ = Profiler.Scope();
			if (!MultiplayerSession.IsHost)
				return;

			MultiplayerPlayer player = MultiplayerSession.GetPlayer(requester);
			if (player == null || !player.TryRestartSaveTransferAfterFallback(out long transferGeneration))
			{
				DebugConsole.LogWarning($"[SaveFileRequest] Rejected stale UDP fallback for {requester}");
				return;
			}
			StartSnapshotTransfer(requester, preferTcp: false, transferGeneration);
		}

		private static bool TryBeginInitialTransfer(ulong requester, out long transferGeneration)
		{
			transferGeneration = 0;
			MultiplayerPlayer player = MultiplayerSession.GetPlayer(requester);
			return player != null && ReadyManager.IsClientInSyncBarrier(requester)
			       && player.TryBeginSaveTransfer(out transferGeneration);
		}

		private static bool TryBeginTransfer(
			ulong requester, string restartTransferId, out long transferGeneration)
		{
			if (string.IsNullOrEmpty(restartTransferId))
				return TryBeginInitialTransfer(requester, out transferGeneration);

			transferGeneration = 0;
			MultiplayerPlayer player = MultiplayerSession.GetPlayer(requester);
			return player != null && ReadyManager.IsClientInSyncBarrier(requester)
			       && player.TryRestartSaveTransfer(restartTransferId, out transferGeneration);
		}

		private static void StartSnapshotTransfer(
			ulong requester, bool preferTcp, long transferGeneration)
		{
			if (!MultiplayerSession.IsHost)
				return;
			MultiplayerPlayer player = MultiplayerSession.GetPlayer(requester);
			if (player == null || !ReadyManager.IsClientInSyncBarrier(requester))
			{
				DebugConsole.LogWarning($"[SaveFileRequest] No active sync barrier for {requester}");
				return;
			}

			SaveFileTransferManager.CancelTransfers(requester);
			RiptideServer riptideServer = NetworkConfig.TransportServer as RiptideServer;
			riptideServer?.TcpTransfer?.CancelTransfers(requester);
			if (!ReadyManager.BeginSnapshotEpoch(requester, out long snapshotGeneration))
			{
				player.CompleteSaveTransfer();
				return;
			}

			try
			{
				string fileName = SaveHelper.WorldName + ".sav";
				byte[] data = SaveHelper.GetWorldSave();
				if (data == null || data.Length <= 0 || data.Length > SaveFileChunkPacket.MaxSaveBytes)
					throw new InvalidDataException($"Invalid save size {data?.Length ?? 0}");

				if (preferTcp && NetworkConfig.IsLanConfig() && riptideServer?.TcpTransfer != null)
				{
					QueueTcpTransfer(
						riptideServer, requester, player, transferGeneration,
						snapshotGeneration, fileName, data);
					return;
				}

				DebugConsole.Log($"[SaveFileRequest] Starting UDP snapshot {snapshotGeneration} for '{fileName}' to {requester}");
				CoroutineRunner.RunOne(StreamChunks(
					data, fileName, requester, snapshotGeneration, transferGeneration));
			}
			catch (Exception ex)
			{
				player.CompleteSaveTransfer();
				ReadyManager.AbortSyncBarrier(requester);
				DebugConsole.LogError($"[SaveFileRequest] Failed to send save file: {ex}");
				NetworkConfig.TransportServer?.KickClient(requester);
			}
		}

		private static void QueueTcpTransfer(
			RiptideServer server,
			ulong requester,
			MultiplayerPlayer player,
			long transferGeneration,
			long snapshotGeneration,
			string fileName,
			byte[] data)
		{
			int tcpPort = Configuration.Instance.Host.LanSettings.Port + 1;
			string transferToken = server.TcpTransfer.QueueTransfer(requester, fileName, data);
			if (!player.TrySetSaveTransferToken(transferGeneration, transferToken))
				throw new InvalidOperationException("Save transfer generation changed while queuing TCP data");
			byte[] fileHash;
			using (SHA256 sha = SHA256.Create())
				fileHash = sha.ComputeHash(data);

			PacketSender.SendToPlayer(requester, new TcpTransferStartPacket
			{
				TcpPort = tcpPort,
				FileName = fileName,
				FileSize = data.Length,
				ClientId = requester,
				SnapshotGeneration = snapshotGeneration,
				TransferToken = transferToken,
				FileHash = fileHash
			});
			DebugConsole.Log($"[SaveFileRequest] Initiated TCP snapshot {snapshotGeneration} for '{fileName}' to {requester}");
		}

        public static void SendSaveFileToAll(IEnumerable<ulong> requesters)
        {
	        using var _ = Profiler.Scope();

            if (!MultiplayerSession.IsHost)
                return;

			foreach (ulong requester in requesters)
				SendSaveFile(requester);
        }


		private static IEnumerator StreamChunks(
			byte[] data,
			string fileName,
			ulong steamID,
			long snapshotGeneration,
			long transferGeneration)
		{
			using var _ = Profiler.Scope();
			if (data == null || data.Length <= 0 || data.Length > SaveFileChunkPacket.MaxSaveBytes)
			{
				DebugConsole.LogError($"[SaveFileRequest] Refusing invalid save size {data?.Length ?? 0}");
				yield break;
			}

			int chunkSize = SaveFileChunkPacket.MaxChunkBytes;
			int totalChunks = (int)Math.Ceiling((double)data.Length / chunkSize);
			byte[] fileHash;
			using (SHA256 sha = SHA256.Create())
				fileHash = sha.ComputeHash(data);

			// Send a bounded pair per frame; the ACK window remains the real backpressure limit.
			int chunksPerFrame = 2;
			int chunksSentThisFrame = 0;

			DebugConsole.Log($"[SaveFileRequest] Starting SECURE transfer of '{fileName}' ({Utils.FormatBytes(data.Length)}) to {steamID} in {totalChunks} chunks.");

			// SUA IDEIA: Registra transferência no manager para rastrear ACKs
			string transferId = Guid.NewGuid().ToString("N");
			MultiplayerPlayer transferPlayer = MultiplayerSession.GetPlayer(steamID);
			if (transferPlayer == null
			    || !transferPlayer.TrySetSaveTransferToken(transferGeneration, transferId))
			{
				DebugConsole.LogWarning(
					$"[SaveFileRequest] Could not bind UDP transfer {snapshotGeneration} for {steamID}");
				yield break;
			}
			SaveFileTransferManager.StartTransfer(
				steamID, transferId, totalChunks);

			for (int offset = 0; offset < data.Length; /* increments manually */)
			{
				MultiplayerPlayer player = MultiplayerSession.GetPlayer(steamID);
				if (player == null || !player.IsCurrentSaveTransfer(transferGeneration))
				{
					DebugConsole.LogWarning($"[SaveFileRequest] Stopped stale UDP snapshot {snapshotGeneration} for {steamID}");
					yield break;
				}

				int chunkIndex = offset / chunkSize;
				var sendDecision = SaveFileTransferManager.GetChunkSendDecision(
					steamID, transferId, chunkIndex);
				if (sendDecision == SaveFileTransferManager.ChunkSendDecision.Stop)
				{
					DebugConsole.LogWarning($"[SaveFileRequest] Stopped inactive UDP snapshot {snapshotGeneration}");
					yield break;
				}
				if (sendDecision == SaveFileTransferManager.ChunkSendDecision.Wait)
				{
					chunksSentThisFrame = 0;
					yield return null;
					continue;
				}

				int size = Math.Min(chunkSize, data.Length - offset);
				byte[] chunk = new byte[size];
				Buffer.BlockCopy(data, offset, chunk, 0, size);

				var chunkPacket = new SaveFileChunkPacket
				{
					FileName = fileName,
					SnapshotGeneration = snapshotGeneration,
					Offset = offset,
					TotalSize = data.Length,
					ChunkSize = chunkSize,
					FileHash = fileHash,
					Chunk = chunk
				};

				// Wrap in secure transfer packet for integrity validation
				var securePacket = new SecureTransferPacket
				{
					SequenceNumber = offset / chunkSize,  // Calculate chunk index from offset
					TransferId = transferId,
					PayloadBytes = SecureTransferPacket.SerializeSaveFileChunk(chunkPacket)
				};

				bool success = PacketSender.SendToPlayer(steamID, securePacket);

				if (success)
				{
					// SUA IDEIA: Marca chunk como enviado no sistema ACK
					SaveFileTransferManager.MarkChunkSent(steamID, transferId, chunkIndex);

					offset += chunkSize; // Only advance if sent successfully
					chunksSentThisFrame++;
					if (chunksSentThisFrame >= chunksPerFrame)
					{
						chunksSentThisFrame = 0;
						yield return null; // Wait for next frame
					}
				}
				else
				{
					// Backpressure: Failed to send (buffer likely full). Wait and retry same offset.
					//DebugConsole.LogWarning($"[SaveFileRequest] Buffer full/Send failed. Retrying...");
					chunksSentThisFrame = 0;
					yield return null;
				}
			}

			DebugConsole.Log($"[SaveFileRequest] SECURE transfer complete. Sent {totalChunks} chunks to {steamID}. Client will validate integrity.");
		}

    }
}
