using System.Collections.Generic;
using System.Linq;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.DuplicantActions;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport;
using ONI_Together.Networking.Transport.Lan;
using ONI_Together.Networking.Transport.Steam;
using Riptide;
using Riptide.Transports.Udp;
using System.Runtime.Serialization;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class TransportTests
	{
		private sealed class SequencedPacketSender : TransportPacketSender
		{
			private readonly Queue<bool> _results;
			public int SendCount { get; private set; }

			public SequencedPacketSender(params bool[] results)
			{
				_results = new Queue<bool>(results);
			}

			public override bool SendPacket(
				object conn,
				IPacket packet,
				PacketSendMode sendType = PacketSendMode.ReliableImmediate)
			{
				SendCount++;
				return _results.Count == 0 || _results.Dequeue();
			}
		}

		private sealed class RecordingTransportServer : TransportServer
		{
			public ulong KickedClientId { get; private set; }
			public int KickCount { get; private set; }

			public override void Prepare() { }
			public override void Start() { }
			public override void Stop() { }
			public override void CloseConnections() { }
			public override void Update() { }
			public override void OnMessageRecieved() { }
			public override void KickClient(ulong clientId)
			{
				KickedClientId = clientId;
				KickCount++;
			}
		}

		private sealed class RemovingTransportServer : TransportServer
		{
			public readonly List<ulong> KickedClientIds = new();

			public override void Prepare() { }
			public override void Start() { }
			public override void Stop() { }
			public override void CloseConnections() { }
			public override void Update() { }
			public override void OnMessageRecieved() { }
			public override void KickClient(ulong clientId)
			{
				KickedClientIds.Add(clientId);
				MultiplayerSession.ConnectedPlayers.Remove(clientId);
			}
		}

		#if DEBUG
		[UnitTest(name: "Reliable bulk: failed page remains terminal until drop", category: "Networking")]
		public static UnitTestResult FailedBulkFlushRetainsPayload()
		{
			TransportPacketSender originalSender = NetworkConfig.TransportPacketSender;
			bool originalQueueSetting = Configuration.Instance.EnablePacketQueue;
			var sender = new SequencedPacketSender(false, true);
			object connection = new();
			int terminations = 0;
			try
			{
				Configuration.Instance.EnablePacketQueue = false;
				NetworkConfig.TransportPacketSender = sender;
				PacketSender.OutgoingPageTerminationForTests = candidate =>
				{
					if (ReferenceEquals(candidate, connection))
						terminations++;
				};
				PacketSender.ResetSessionState();
				PacketSender.SendToConnection(connection, new DuplicantCarryItemPacket());
				PacketSender.DispatchPendingBulkPackets();
				int retained = PacketSender.PendingBulkCountForTests(connection);
				PacketSender.DispatchPendingBulkPackets();
				PacketSender.DropConnection(connection);

				return sender.SendCount == 1 && terminations == 1 && retained == 1
				       && PacketSender.PendingBulkCountForTests(connection) == 0
					? UnitTestResult.Pass("Failed page terminated once and retained bulk state until connection drop")
					: UnitTestResult.Fail(
						$"Failed bulk state was inconsistent: sends={sender.SendCount}, terminations={terminations}, retained={retained}");
			}
			finally
			{
				PacketSender.ResetSessionState();
				PacketSender.OutgoingPageTerminationForTests = null;
				NetworkConfig.TransportPacketSender = originalSender;
				Configuration.Instance.EnablePacketQueue = originalQueueSetting;
			}
		}
		#endif

		[UnitTest(name: "Reliable broadcast: failed send disconnects peer", category: "Networking")]
		public static UnitTestResult FailedReliableBroadcastDisconnectsPeer()
		{
			TransportPacketSender originalSender = NetworkConfig.TransportPacketSender;
			TransportServer originalServer = NetworkConfig.TransportServer;
			bool originalQueueSetting = Configuration.Instance.EnablePacketQueue;
			ulong originalHostId = MultiplayerSession.HostUserID;
			bool originalIsHost = MultiplayerSession.IsHost;
			var originalPlayers = new Dictionary<ulong, MultiplayerPlayer>(MultiplayerSession.ConnectedPlayers);
			var server = new RecordingTransportServer();
			try
			{
				PrepareFailedBroadcast(new SequencedPacketSender(false), server);
				PacketSender.SendToAllClients(new AllClientsReadyPacket(), PacketSendMode.Reliable);
				return server.KickedClientId == 2
					? UnitTestResult.Pass("Reliable broadcast failure disconnected the desynchronized peer")
					: UnitTestResult.Fail("Reliable broadcast failure was silently ignored");
			}
			finally
			{
				RestoreBroadcastState(
					originalSender, originalServer, originalQueueSetting,
					originalHostId, originalIsHost, originalPlayers);
			}
		}

		[UnitTest(name: "Reliable broadcast tolerates synchronous peer removal", category: "Networking")]
		public static UnitTestResult BroadcastUsesStablePlayerSnapshot()
		{
			TransportPacketSender originalSender = NetworkConfig.TransportPacketSender;
			TransportServer originalServer = NetworkConfig.TransportServer;
			bool originalQueueSetting = Configuration.Instance.EnablePacketQueue;
			ulong originalHostId = MultiplayerSession.HostUserID;
			bool originalIsHost = MultiplayerSession.IsHost;
			var originalPlayers = new Dictionary<ulong, MultiplayerPlayer>(MultiplayerSession.ConnectedPlayers);
			var server = new RemovingTransportServer();
			try
			{
				PrepareFailedBroadcast(new SequencedPacketSender(false, false), server);
				AddReadyPlayer(3);
				PacketSender.SendToAllClients(new AllClientsReadyPacket(), PacketSendMode.Reliable);
				PrepareFailedBroadcast(new SequencedPacketSender(false, false), server);
				AddReadyPlayer(3);
				PacketSender.SendToAllExcluding(
					new AllClientsReadyPacket(), new HashSet<ulong>(), PacketSendMode.Reliable);
				return server.KickedClientIds.Count == 4
				       && server.KickedClientIds.Count(id => id == 2) == 2
				       && server.KickedClientIds.Count(id => id == 3) == 2
					? UnitTestResult.Pass("Both broadcast paths disconnect each synchronously removed peer once")
					: UnitTestResult.Fail(
						$"Broadcast skipped or repeatedly disconnected peers: {string.Join(",", server.KickedClientIds)}");
			}
			finally
			{
				RestoreBroadcastState(originalSender, originalServer, originalQueueSetting,
					originalHostId, originalIsHost, originalPlayers);
			}
		}

		[UnitTest(name: "Reliable backlog overflow terminates once", category: "Networking")]
		public static UnitTestResult BacklogOverflowTerminatesOnce()
		{
			TransportPacketSender originalSender = NetworkConfig.TransportPacketSender;
			TransportServer originalServer = NetworkConfig.TransportServer;
			bool originalQueueSetting = Configuration.Instance.EnablePacketQueue;
			ulong originalHostId = MultiplayerSession.HostUserID;
			bool originalIsHost = MultiplayerSession.IsHost;
			var originalPlayers = new Dictionary<ulong, MultiplayerPlayer>(MultiplayerSession.ConnectedPlayers);
			var server = new RecordingTransportServer();
			try
			{
				ReadyManager.ResetSessionState();
				NetworkConfig.TransportServer = server;
				MultiplayerSession.HostUserID = 1;
				MultiplayerSession.IsHost = true;
				MultiplayerSession.ConnectedPlayers.Clear();
				var player = new MultiplayerPlayer(2);
				player.BeginConnection(new object());
				player.ProtocolVerified = true;
				MultiplayerSession.ConnectedPlayers.Add(2, player);
				if (!ReadyManager.BeginSyncBarrier(2))
					return UnitTestResult.Fail("Could not arrange loading barrier");

				var invalid = new DeferredReliablePacket(System.Array.Empty<byte>());
				PacketSender.SendToAllClients(invalid, PacketSendMode.Reliable);
				PacketSender.SendToAllClients(invalid, PacketSendMode.Reliable);
				if (server.KickCount != 1 || ReadyManager.IsClientInSyncBarrier(2)
				    || ReliableSyncBacklog.CanTransfer(2, 2))
					return UnitTestResult.Fail("Overflow was repeated or retained a poisoned loading epoch");
				return UnitTestResult.Pass("First overflow clears the epoch and disconnects exactly once");
			}
			finally
			{
				ReadyManager.ResetSessionState();
				RestoreBroadcastState(originalSender, originalServer, originalQueueSetting,
					originalHostId, originalIsHost, originalPlayers);
			}
		}

		private static void PrepareFailedBroadcast(
			TransportPacketSender sender, TransportServer server)
		{
			Configuration.Instance.EnablePacketQueue = false;
			NetworkConfig.TransportPacketSender = sender;
			NetworkConfig.TransportServer = server;
			MultiplayerSession.HostUserID = 1;
			MultiplayerSession.IsHost = true;
			MultiplayerSession.ConnectedPlayers.Clear();
			AddReadyPlayer(2);
		}

		private static void AddReadyPlayer(ulong playerId)
		{
			var player = new MultiplayerPlayer(playerId);
			player.BeginConnection(new object());
			player.ProtocolVerified = true;
			player.readyState = ClientReadyState.Ready;
			MultiplayerSession.ConnectedPlayers.Add(playerId, player);
		}

		private static void RestoreBroadcastState(
			TransportPacketSender sender,
			TransportServer server,
			bool queueSetting,
			ulong hostId,
			bool isHost,
			Dictionary<ulong, MultiplayerPlayer> players)
		{
			MultiplayerSession.ConnectedPlayers.Clear();
			foreach (var pair in players)
				MultiplayerSession.ConnectedPlayers.Add(pair.Key, pair.Value);
			MultiplayerSession.HostUserID = hostId;
			MultiplayerSession.IsHost = isHost;
			NetworkConfig.TransportPacketSender = sender;
			NetworkConfig.TransportServer = server;
			Configuration.Instance.EnablePacketQueue = queueSetting;
		}

		[UnitTest(name: "Transport server/client types match NetworkConfig", category: "Transport", liveSafe: true)]
		public static UnitTestResult TransportMatchesConfig()
		{
			var transport = NetworkConfig.transport;
			var server = NetworkConfig.TransportServer;
			var client = NetworkConfig.TransportClient;

			if (server == null)
				return UnitTestResult.Fail("TransportServer is null");
			if (client == null)
				return UnitTestResult.Fail("TransportClient is null");

			switch (transport)
			{
				case NetworkConfig.NetworkTransport.RIPTIDE:
					if (server is not RiptideServer)
						return UnitTestResult.Fail($"transport=RIPTIDE but server is {server.GetType().Name}");
					if (client is not RiptideClient)
						return UnitTestResult.Fail($"transport=RIPTIDE but client is {client.GetType().Name}");
					return UnitTestResult.Pass("RIPTIDE config matches RiptideServer/RiptideClient");

				case NetworkConfig.NetworkTransport.STEAMWORKS:
					if (server is not SteamworksServer)
						return UnitTestResult.Fail($"transport=STEAMWORKS but server is {server.GetType().Name}");
					if (client is not SteamworksClient)
						return UnitTestResult.Fail($"transport=STEAMWORKS but client is {client.GetType().Name}");
					return UnitTestResult.Pass("STEAMWORKS config matches SteamworksServer/SteamworksClient");

				default:
					return UnitTestResult.Fail($"Unknown transport: {transport}");
			}
		}

		[UnitTest(name: "Riptide timeout is 30000 ms", category: "Transport", liveSafe: true)]
		public static UnitTestResult RiptideTimeoutCorrect()
		{
			if (!NetworkConfig.IsLanConfig())
				return UnitTestResult.Skip("Requires Riptide/LAN transport");

			const int ExpectedTimeoutMs = 30000;

			Connection connection;
			if (MultiplayerSession.IsHost)
			{
				var server = RiptideServer.ServerInstance;
				if (server == null)
					return UnitTestResult.Fail("Riptide Server instance is null");

				connection = server.Clients.FirstOrDefault();
				if (connection == null)
					return UnitTestResult.Fail("Server has no client connections to read timeout from");
			}
			else if (MultiplayerSession.IsClient)
			{
				var client = RiptideClient.Client;
				if (client?.Connection == null)
					return UnitTestResult.Fail("Riptide Client connection is null");

				connection = client.Connection;
			}
			else
			{
				return UnitTestResult.Skip("Requires an active multiplayer session");
			}

			if (connection.TimeoutTime != ExpectedTimeoutMs)
				return UnitTestResult.Fail($"Connection.TimeoutTime is {connection.TimeoutTime} ms, expected {ExpectedTimeoutMs} ms (might be set in seconds instead of ms)");

			string role = MultiplayerSession.IsHost ? "Server" : "Client";
			return UnitTestResult.Pass($"Riptide {role} timeout = {connection.TimeoutTime} ms");
		}

		[UnitTest(
			name: "Riptide pending handshake survives world-load stalls",
			category: "Transport")]
		public static UnitTestResult PendingHandshakeUsesResilientConnection()
		{
			Connection connection = (Connection)FormatterServices.GetUninitializedObject(
				typeof(UdpConnection));
			RiptideServer.ConfigureConnectionForHandshake(connection);
			return !connection.CanQualityDisconnect
			       && connection.MaxSendAttempts == 30
			       && connection.MaxAvgSendAttempts == 12
			       && connection.AvgSendAttemptsResilience == 128
				? UnitTestResult.Pass(
					"Pending Welcome uses the same resilience as connected peers")
				: UnitTestResult.Fail(
					"Pending connection retained Riptide's short quality-disconnect window");
		}

		[UnitTest(name: "Connection stable", category: "Transport", liveSafe: true)]
		public static UnitTestResult ConnectionStable()
		{
			if (!MultiplayerSession.InSession)
				return UnitTestResult.Skip("Requires an active multiplayer session");

			if (!NetworkConfig.IsLanConfig())
				return UnitTestResult.Skip("Requires Riptide/LAN transport");

			if (MultiplayerSession.IsHost)
			{
				var server = RiptideServer.ServerInstance;
				if (server == null || !server.IsRunning)
					return UnitTestResult.Fail("Riptide Server is not running");

				int connected = 0;
				foreach (var connection in server.Clients)
				{
					if (!connection.IsNotConnected)
						connected++;
				}

				if (connected == 0)
					return UnitTestResult.Fail("No active connections on server");

				return UnitTestResult.Pass($"Server running with {connected} active connection(s)");
			}

			var client = RiptideClient.Client;
			if (client == null)
				return UnitTestResult.Fail("Riptide Client instance is null");
			if (!client.IsConnected)
				return UnitTestResult.Fail("Riptide Client is not connected");

			int rtt = client.SmoothRTT;
			return UnitTestResult.Pass($"Client connected, smoothed RTT = {rtt} ms");
		}
	}
}
