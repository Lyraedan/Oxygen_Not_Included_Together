using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport.Lan;
using ONI_Together.UI;
using Steamworks;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Core
{
	class ClientReadyStatusPacket : IPacket
	{
		internal const int MaxPlayerNameChars = 128;
		public ulong SenderId;
		public ClientReadyState Status = ClientReadyState.Unready;
		public string PlayerName = string.Empty;
		public ulong ReconnectToken;
		public long SnapshotGeneration;

		public ClientReadyStatusPacket() { }

		public ClientReadyStatusPacket(ulong senderId, ClientReadyState status)
		{
			using var _ = Profiler.Scope();

			SenderId = senderId;
			Status = status;
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write((int)Status);
			writer.Write(SenderId);
			writer.Write(PlayerName ?? string.Empty);
			writer.Write(ReconnectToken);
			writer.Write(SnapshotGeneration);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Status = (ClientReadyState)reader.ReadInt32();
			SenderId = reader.ReadUInt64();
			PlayerName = reader.ReadString();
			if (PlayerName.Length > MaxPlayerNameChars)
				throw new InvalidDataException("Ready-state player name is too long");
			ReconnectToken = reader.ReadUInt64();
			SnapshotGeneration = reader.ReadInt64();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			if (!SyncBarrier.IsValidReadyState(Status))
			{
				DebugConsole.LogWarning($"[ClientReadyStatusPacket] Rejected invalid ready state {(int)Status}");
				return;
			}

			if (!MultiplayerSession.IsHost)
			{
				if (string.IsNullOrEmpty(PlayerName))
					return;

				MultiplayerSession.KnownPlayerNames[SenderId] = PlayerName;

				if (SenderId == MultiplayerSession.HostUserID)
				{
					var host = MultiplayerSession.GetPlayer(SenderId);
					if (host != null)
					{
						host.PlayerName = PlayerName;
					}
				}
				else
				{
					var client = NetworkConfig.TransportClient as RiptideClient;
					bool isLoading = client != null && SenderId == MultiplayerSession.LocalUserID && client.IsLoadingReconnect;
					if (isLoading)
					{
						client.IsLoadingReconnect = false;
					}
					else
					{
						var pending = ChatScreen.GeneratePendingMessage(
							string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_JOINED, PlayerName));
						ChatScreen.QueueMessage(pending);
					}
				}
				return;
			}
			if (!SyncBarrier.SenderMatches(SenderId, PacketHandler.CurrentContext.SenderId))
			{
				DebugConsole.LogWarning($"[ClientReadyStatusPacket] Rejected spoofed sender {SenderId} from {PacketHandler.CurrentContext.SenderId}");
				return;
			}

			MultiplayerPlayer player;
			MultiplayerSession.ConnectedPlayers.TryGetValue(SenderId, out player);

			if (player == null)
			{
				DebugConsole.LogError("Tried to update ready state for a null player", false);
				return;
			}

			if (Status == ClientReadyState.Aborted)
			{
				if (!ReadyManager.TryAbortClientWorldLoad(
					    SenderId, ReconnectToken, SnapshotGeneration))
				{
					DebugConsole.LogWarning(
						$"[ClientReadyStatusPacket] Rejected invalid abort proof from {SenderId}");
					return;
				}
				DebugConsole.LogWarning(
					$"[ClientReadyStatusPacket] Client {SenderId} aborted world loading");
				NetworkConfig.TransportServer?.KickClient(SenderId);
				ReadyManager.RefreshScreen();
				ReadyManager.RefreshReadyState();
				return;
			}

			if (Status == ClientReadyState.Loading)
			{
				if (!ReadyManager.SetPlayerReadyState(
					    player, Status, ReconnectToken, SnapshotGeneration))
				{
					DebugConsole.LogWarning($"[ClientReadyStatusPacket] Rejected loading proof from {SenderId}");
					return;
				}
				var server = NetworkConfig.TransportServer as RiptideServer;
				server?.MarkClientLoading(SenderId, ReconnectToken);
				if (!PacketSender.SendToPlayer(SenderId, new LoadingAcceptedPacket
				{
					ReconnectToken = ReconnectToken,
					SnapshotGeneration = SnapshotGeneration
				}, PacketSendMode.ReliableImmediate))
				{
					DebugConsole.LogWarning($"[ClientReadyStatusPacket] Failed to acknowledge loading for {SenderId}");
					ReadyManager.AbortSyncBarrier(SenderId);
					NetworkConfig.TransportServer?.KickClient(SenderId);
					return;
				}
				ReadyManager.RefreshScreen();
				ReadyManager.RefreshReadyState();
				return;
			}

			if (!ReadyManager.SetPlayerReadyState(
				    player, Status, ReconnectToken, SnapshotGeneration))
			{
				DebugConsole.LogWarning($"[ClientReadyStatusPacket] Rejected {Status} transition from {SenderId}");
				return;
			}

			bool nameChanged = !string.IsNullOrEmpty(PlayerName) && player.PlayerName != PlayerName;
			if (nameChanged)
			{
				player.PlayerName = PlayerName;
			}

			DebugConsole.Log($"[ClientReadyStatusPacket] {SenderId} marked as {Status}");

			if (NetworkConfig.IsLanConfig() && nameChanged)
			{
				var server = NetworkConfig.TransportServer as RiptideServer;
				bool isLoadingReconnect = server != null && server.ConsumeReconnectFromLoad(SenderId);

				if (!isLoadingReconnect)
				{
					var pending = ChatScreen.GeneratePendingMessage(
						string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_JOINED, player.PlayerName));
					ChatScreen.QueueMessage(pending);
				}

				PacketSender.SendToAllClients(new ClientReadyStatusPacket
				{
					SenderId = MultiplayerSession.HostUserID,
					PlayerName = Utils.GetLocalPlayerName()
				});

				PacketSender.SendToAllClients(new ClientReadyStatusPacket
				{
					SenderId = SenderId,
					PlayerName = player.PlayerName
				});
			}

			ReadyManager.RefreshScreen();
			bool allReady = ReadyManager.IsEveryoneReady();
            DebugConsole.Log($"[ClientReadyStatusPacket] Is everyone ready? {allReady}");
			ReadyManager.RefreshReadyState();
		}
	}
}
