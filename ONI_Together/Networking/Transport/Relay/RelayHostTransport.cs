using System;
using System.Linq;
using LiteNetLib;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.Networking.Overlay;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Transport.Lan;
using Shared.Networking.Dedicated;
using Shared.Profiling;

namespace ONI_Together.Networking.Transport.Relay
{
	public sealed class RelayPeer
	{
		public readonly ulong ClientId;

		public RelayPeer(ulong clientId)
		{
			ClientId = clientId;
		}
	}

	public sealed class RelayHostTransport : LiteNetLibServer
	{
		private readonly TransportClient _link;

		public RelayHostTransport(TransportClient link)
		{
			_link = link;
		}

		public override void Start()
		{
			using var _ = Profiler.Scope();

			UseHostId();
			MultiplayerSession.SetHost(1);
			MultiplayerSession.InActiveSession = true;

			ClientList.Clear();
			ClientList.Add(1);

			var host = new MultiplayerPlayer(1)
			{
				PlayerName = Utils.GetLocalPlayerName(),
				Connection = null
			};
			MultiplayerSession.ConnectedPlayers[1] = host;
			MultiplayerSession.KnownPlayerNames[1] = host.PlayerName;

			PacketHandler.readyToProcess = true;
			OxySyncChat.AddSystemMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_SERVER_STARTED, "Dedicated Server"));
		}

		public void PeerJoined(ulong clientId)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.ConnectedPlayers.TryGetValue(clientId, out var player))
			{
				player = new MultiplayerPlayer(clientId);
				MultiplayerSession.ConnectedPlayers[clientId] = player;
			}
			player.Connection = new RelayPeer(clientId);

			if (!ClientList.Contains(clientId))
				ClientList.Add(clientId);

			DebugConsole.Log($"[RelayHost] Remote client connected: {clientId}");
		}

		public void PeerLeft(ulong clientId)
		{
			using var _ = Profiler.Scope();

			ClientList.Remove(clientId);

			if (MultiplayerSession.ConnectedPlayers.TryGetValue(clientId, out var player))
			{
				player.Connection = null;
				MultiplayerSession.ConnectedPlayers.Remove(clientId);
				DebugConsole.Log($"[RelayHost] Player {clientId} disconnected.");
			}

			ReadyManager.RefreshReadyState();
			MultiplayerSession.RefreshAllPlayerCursors();
		}

		public override void Update()
		{
			using var _ = Profiler.Scope();

			_link.Update();
		}

		public override void OnMessageRecieved()
		{
		}

		public override void KickClient(ulong clientId)
		{
			using var _ = Profiler.Scope();

			if (clientId == 1 || !ClientList.Contains(clientId))
				return;

			RelaySession.SendFrame(RelayProtocol.PeerEvent(RelayFrameType.KickPeer, clientId));
			ClientList.Remove(clientId);
			MultiplayerSession.ConnectedPlayers.Remove(clientId);
			DebugConsole.Log($"[RelayHost] Kicked client: {clientId}");
		}

		public override void CloseConnections()
		{
			using var _ = Profiler.Scope();

			foreach (ulong clientId in ClientList.Where(id => id != 1).ToList())
				KickClient(clientId);
		}

		public override void Stop()
		{
			using var _ = Profiler.Scope();

			ClientList.Clear();

			_link.Disconnect();
			NetworkConfig.RestoreDirectLanHost();

			MultiplayerSession.HostUserID = Utils.NilUlong();
			MultiplayerSession.InActiveSession = false;

			DebugConsole.Log("[RelayHost] Stopped hosting through the relay.");
		}
	}

	public sealed class RelayPacketSender : LiteNetLibPacketSender
	{
		public override bool SendPacket(object conn, IPacket packet, PacketSendMode sendType = PacketSendMode.ReliableImmediate)
		{
			using var _ = Profiler.Scope();

			if (conn is not RelayPeer)
				return base.SendPacket(conn, packet, sendType);

			byte[] bytes = PacketSender.SerializePacketForSending(packet);

			if (packet is IPriorityPacket)
				sendType |= PacketSendMode.Priority;

			return SendChunkedIfNeeded(conn, bytes, packet, sendType, SendThroughRelay);
		}

		private bool SendThroughRelay(object conn, byte[] bytes, IPacket packet, PacketSendMode sendType)
		{
			var target = (RelayPeer)conn;
			DeliveryMethod deliveryMethod = ConvertSendType(sendType, packet);
			byte channel = IsPriority(sendType, packet) ? PriorityChannel : DefaultChannel;

			if (!RelaySession.SendFrame(RelayProtocol.PeerData(RelayFrameType.ToPeer, target.ClientId, bytes), channel, deliveryMethod))
				return false;

			PacketTracker.TrackSent(new PacketTracker.PacketTrackData
			{
				packet = packet,
				size = bytes.Length
			});
			NetIdActivityTracker.RecordOutgoing(packet, bytes.Length);
			return true;
		}
	}
}
