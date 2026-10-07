using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport.Lan;
using ONI_Together.Networking.Transport.Steamworks;
using ONI_Together.Networking.OxySync.Components;
using Shared.Profiling;
using System.IO;

namespace ONI_Together.Networking.Packets.Core
{
	class ClientReadyStatusPacket : IPacket, IAllowedWithoutWorldPacket
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
						host.PlayerName = PlayerName;
				}
				else
				{
					OxySyncChat.AddSystemMessage(
						string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_JOINED, PlayerName));
				}
				return;
			}

			if (!MultiplayerSession.ConnectedPlayers.TryGetValue(SenderId, out MultiplayerPlayer player))
			{
				DebugConsole.LogError($"Tried to update ready state for unknown player {SenderId}", false);
				return;
			}

			if (Status == ClientReadyState.Loading)
			{
				DebugConsole.LogWarning($"[ClientReadyStatusPacket] ignored client-originated Loading status from {SenderId}; only the host may begin synchronization.");
				return;
			}

			if (Status == ClientReadyState.Ready)
			{
				if (player.readyState == ClientReadyState.Ready)
					return;

				if (!ReadyManager.IsSynchronizing || player.readyState != ClientReadyState.Loading)
				{
					DebugConsole.LogWarning($"[ClientReadyStatusPacket] rejected Ready for PlayerId={SenderId} while synchronization={ReadyManager.IsSynchronizing} state={player.readyState}");
					return;
				}
			}
			else if (ReadyManager.IsSynchronizing && player.readyState != ClientReadyState.Unready)
			{
				return;
			}

			bool nameChanged = !string.IsNullOrEmpty(PlayerName) && player.PlayerName != PlayerName;
			if (nameChanged)
				player.PlayerName = PlayerName;

			ReadyManager.SetPlayerReadyState(player, Status);
			if (Status == ClientReadyState.Unready)
				ReadyManager.TrackPendingJoin(player.PlayerId, player.PlayerName, Status);
			else if (Status == ClientReadyState.Ready)
				ReadyManager.CompletePendingJoin(player.PlayerId);

			DebugConsole.Log($"[ClientReadyStatusPacket] {SenderId} marked as {Status}");

			if (NetworkConfig.IsLanConfig() && nameChanged)
			{
				OxySyncChat.AddSystemMessage(
					string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_JOINED, player.PlayerName));

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
			ReadyManager.RefreshReadyState();
		}
	}
}
