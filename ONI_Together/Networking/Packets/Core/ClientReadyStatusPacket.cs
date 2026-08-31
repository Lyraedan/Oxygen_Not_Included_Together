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
	/// <summary>
	/// Host -&gt; client player name / join announcement. Clients no longer send this for ready
	/// state - they report readiness via ReadyStateSyncer commands and a periodic heartbeat.
	/// </summary>
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
				return;
			}

			var client = NetworkConfig.TransportClient as LiteNetLibClient;
			bool isLoading = client != null && SenderId == MultiplayerSession.LocalUserID && client.IsLoadingReconnect;
			if (isLoading)
			{
				client.IsLoadingReconnect = false;
				return;
			}

			OxySyncChat.AddSystemMessage(
				string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_JOINED, PlayerName));
		}
	}
}