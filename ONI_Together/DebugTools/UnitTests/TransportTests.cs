using System.Linq;
using ONI_Together.Networking;
using ONI_Together.Networking.Transport.Lan;
using ONI_Together.Networking.Transport.Steam;
using Riptide;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class TransportTests
	{
		[UnitTest(name: "Transport server/client types match NetworkConfig", category: "Transport")]
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

		[UnitTest(name: "Riptide timeout is 30000 ms", category: "Transport")]
		public static UnitTestResult RiptideTimeoutCorrect()
		{
			if (!NetworkConfig.IsLanConfig())
				return UnitTestResult.Fail("Not on Riptide/LAN transport");

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
				return UnitTestResult.Fail("Neither host nor client");
			}

			if (connection.TimeoutTime != ExpectedTimeoutMs)
				return UnitTestResult.Fail($"Connection.TimeoutTime is {connection.TimeoutTime} ms, expected {ExpectedTimeoutMs} ms (might be set in seconds instead of ms)");

			string role = MultiplayerSession.IsHost ? "Server" : "Client";
			return UnitTestResult.Pass($"Riptide {role} timeout = {connection.TimeoutTime} ms");
		}

		[UnitTest(name: "Connection stable", category: "Transport")]
		public static UnitTestResult ConnectionStable()
		{
			if (!MultiplayerSession.InSession)
				return UnitTestResult.Fail("Not in a multiplayer session");

			if (!NetworkConfig.IsLanConfig())
				return UnitTestResult.Fail("Stability check only implemented for Riptide transport");

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
