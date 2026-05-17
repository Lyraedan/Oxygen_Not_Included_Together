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

namespace ONI_Together.Networking.Packets.World
{
	public class SaveFileRequestPacket : IPacket
	{
		public ulong Requester;

		public const float SAVE_DATA_SEND_DELAY = 0.05f;

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

			DebugConsole.Log($"[Packets/SaveFileRequest] Received request from {Requester}");
			//GoogleDriveUtils.UploadAndSendToClient(Requester);
			SendSaveFile(Requester);
		}

		public static void SendSaveFile(ulong requester)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;

			try
			{
				string name = SaveHelper.WorldName;
				byte[] data = SaveHelper.GetWorldSave();
				string fileName = name + ".sav";

				if (NetworkConfig.IsLanConfig() && NetworkConfig.TransportServer is RiptideServer riptideServer && riptideServer.TcpTransfer != null)
				{
					int tcpPort = Configuration.Instance.Host.LanSettings.Port + 1;
					riptideServer.TcpTransfer.QueueTransfer(requester, fileName, data);

					var startPacket = new TcpTransferStartPacket
					{
						TcpPort = tcpPort,
						FileName = fileName,
						FileSize = data.Length,
						ClientId = requester
					};
					PacketSender.SendToPlayer(requester, startPacket);
					DebugConsole.Log($"[SaveFileRequest] Initiated TCP transfer for '{fileName}' to {requester}");
				}
				else
				{
					CoroutineRunner.RunOne(StreamChunks(data, fileName, requester));
				}
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[SaveFileRequest] Failed to send save file: {ex}");
			}
		}

		public static void SendSaveFileViaUdp(ulong requester)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;

			try
			{
				string name = SaveHelper.WorldName;
				byte[] data = SaveHelper.GetWorldSave();
				string fileName = name + ".sav";

				DebugConsole.Log($"[SaveFileRequest] Starting UDP fallback transfer for '{fileName}' to {requester}");
				CoroutineRunner.RunOne(StreamChunks(data, fileName, requester));
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[SaveFileRequest] Failed to send save file via UDP fallback: {ex}");
			}
		}

        public static void SendSaveFileToAll()
        {
	        using var _ = Profiler.Scope();

            if (!MultiplayerSession.IsHost)
                return;

            foreach(var player in MultiplayerSession.ConnectedPlayers)
			{
				if (player.Key != MultiplayerSession.HostUserID) {
                    SendSaveFile(player.Key);
                }
            }
        }


        private static IEnumerator StreamChunks(byte[] data, string fileName, ulong steamID)
		{
			using var _ = Profiler.Scope();

			int chunkSize = SaveHelper.SAVEFILE_CHUNKSIZE_KB * 1024;
			int totalChunks = (int)Math.Ceiling((double)data.Length / chunkSize);

			// Optimization: Send multiple chunks per frame to maximize throughput
			// 2 chunks * 256KB = 512KB per frame. At 60FPS -> ~30MB/s theoretical max.
			int chunksPerFrame = 2;
			int chunksSentThisFrame = 0;

			DebugConsole.Log($"[SaveFileRequest] Starting SECURE transfer of '{fileName}' ({Utils.FormatBytes(data.Length)}) to {steamID} in {totalChunks} chunks.");

			// SUA IDEIA: Registra transferência no manager para rastrear ACKs
			string transferId = fileName.Replace(" ", "_").Replace(".sav", "");
			SaveFileTransferManager.StartTransfer(steamID, transferId, fileName, data, chunkSize);

			for (int offset = 0; offset < data.Length; /* increments manually */)
			{
				int size = Math.Min(chunkSize, data.Length - offset);
				byte[] chunk = new byte[size];
				Buffer.BlockCopy(data, offset, chunk, 0, size);

				var chunkPacket = new SaveFileChunkPacket
				{
					FileName = fileName,
					Offset = offset,
					TotalSize = data.Length,
					Chunk = chunk
				};

				// Wrap in secure transfer packet for integrity validation
				var securePacket = new SecureTransferPacket
				{
					SequenceNumber = offset / chunkSize,  // Calculate chunk index from offset
					TransferId = fileName.Replace(" ", "_").Replace(".sav", ""),
					PayloadBytes = SecureTransferPacket.SerializeSaveFileChunk(chunkPacket)
				};

				bool success = PacketSender.SendToPlayer(steamID, securePacket);

				if (success)
				{
					// SUA IDEIA: Marca chunk como enviado no sistema ACK
					int chunkIndex = offset / chunkSize;
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
