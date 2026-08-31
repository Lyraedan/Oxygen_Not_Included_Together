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
				// Tracked on the base transport, not on one concrete server: the client is
				// about to drop its connection to load, and every transport needs the host to
				// keep it gated until it comes back.
				//
				// Handled BEFORE the roster lookup on purpose. The client sends this
				// immediately before closing its connection, so the notice regularly arrives
				// after the transport has already removed it from ConnectedPlayers - the
				// disconnect and this packet land microseconds apart. Requiring a live roster
				// entry here dropped the notice on exactly the path it exists for, leaving
				// PendingLoadingClientCount at zero for the whole load window.
				NetworkConfig.TransportServer?.MarkClientLoading(SenderId);
				DebugConsole.Log(
					$"[ClientReadyStatusPacket] {SenderId} marked as Loading (pending off-roster loads: " +
					$"{NetworkConfig.TransportServer?.PendingLoadingClientCount ?? 0})");

				// The disconnect that follows (or already happened) drives its own
				// RefreshReadyState. If it ran first it saw only the host left and took the
				// "everyone is ready" shortcut, closing the ready screen and opening the
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

			// Announce the join when the player is actually in, not when they first appear.
			// Steam used to say it on lobby entry (SteamLobby), which is a whole join ahead of
			// the truth - measured at 06:57:40 entering the lobby against 06:58:18 reaching the
			// world, so the line landed while they were still watching the ready screen. Ready
			// is the first moment the statement is true.
			//
			// JoinAnnounced keeps a returning loader quiet: it is already set from their first
			// join, and it is cleared only by the roster entry being dropped, which is what
			// leaving for real does.
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
				// LAN path prints the joined line, so gating the consume on IsLanConfig left
				// Steam entries standing for the life of the session. Harmless in size - a set
				// keyed by client id holds at most one entry per player - but it made
				// ConsumeReconnectFromLoad answer about a reconnect that happened long ago.
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
