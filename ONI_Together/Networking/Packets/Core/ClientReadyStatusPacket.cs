using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport.Lan;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.UI;
using Steamworks;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Core
{
	class ClientReadyStatusPacket : IPacket
	{
		public ulong SenderId;
		public ClientReadyState Status = ClientReadyState.Unready;
		public string PlayerName = string.Empty;

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
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Status = (ClientReadyState)reader.ReadInt32();
			SenderId = reader.ReadUInt64();
			PlayerName = reader.ReadString();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

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
					var client = NetworkConfig.TransportClient as LiteNetLibClient;
					bool isLoading = client != null && SenderId == MultiplayerSession.LocalUserID && client.IsLoadingReconnect;
					if (isLoading)
					{
						client.IsLoadingReconnect = false;
					}
					else
					{
					OxySyncChat.AddSystemMessage(
						string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_JOINED, PlayerName));
					}
				}
				return;
			}

			if (Status == ClientReadyState.Loading)
			{
				// Tracked on the base transport: every transport needs the host to keep this
				// client gated until it comes back. Must run BEFORE the roster lookup - the
				// client sends this immediately before closing its connection, so it often
				// arrives after the transport already dropped it from ConnectedPlayers.
				NetworkConfig.TransportServer?.MarkClientLoading(SenderId);
				DebugConsole.Log(
					$"[ClientReadyStatusPacket] {SenderId} marked as Loading (pending off-roster loads: " +
					$"{NetworkConfig.TransportServer?.PendingLoadingClientCount ?? 0})");

				// The disconnect drives its own RefreshReadyState, but if it ran first it saw
				// only the host left, took the "everyone is ready" shortcut and opened the
				// resume gate. Recompute now that the pending load is on record.
				ReadyManager.RefreshScreen();
				ReadyManager.RefreshReadyState();
				return;
			}

			MultiplayerPlayer player;
			MultiplayerSession.ConnectedPlayers.TryGetValue(SenderId, out player);

			if (player == null)
			{
				DebugConsole.LogError("Tried to update ready state for a null player", false);
				return;
			}

			bool nameChanged = !string.IsNullOrEmpty(PlayerName) && player.PlayerName != PlayerName;
			if (nameChanged)
			{
				player.PlayerName = PlayerName;
			}

            ReadyManager.SetPlayerReadyState(player, Status);
			DebugConsole.Log($"[ClientReadyStatusPacket] {SenderId} marked as {Status}");

			// Ready is the first moment "joined" is true. Do not move this back to Steam lobby
			// entry (SteamLobby): that announces a whole join early, while the player is still
			// watching the ready screen. JoinAnnounced keeps a returning loader quiet - it is
			// cleared only by the roster entry being dropped, i.e. by leaving for real.
			if (!NetworkConfig.IsLanConfig()
				&& Status == ClientReadyState.Ready
				&& !player.JoinAnnounced)
			{
				player.JoinAnnounced = true;
				OxySyncChat.AddSystemMessage(
					string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_JOINED, player.PlayerName));
			}

			if (nameChanged)
			{
				// Consume on every transport, not just LAN. The flag is one-shot and only the
				// LAN branch below prints a joined line, so gating the consume on IsLanConfig
				// left Steam entries standing all session, answering about a stale reconnect.
				bool isLoadingReconnect =
					NetworkConfig.TransportServer?.ConsumeReconnectFromLoad(SenderId) == true;

				if (NetworkConfig.IsLanConfig())
				{
					if (!isLoadingReconnect)
					{
						OxySyncChat.AddSystemMessage(
							string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_JOINED, player.PlayerName));
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
			}

			ReadyManager.RefreshScreen();
			bool allReady = ReadyManager.IsEveryoneReady();
            DebugConsole.Log($"[ClientReadyStatusPacket] Is everyone ready? {allReady}");
			ReadyManager.RefreshReadyState();
		}
	}
}
