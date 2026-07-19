using System.Collections.Generic;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Transport.Lan;

namespace ONI_Together.DebugTools.UnitTests;

public static class RiptideSessionEpochTests
{
	[UnitTest(name: "LAN client: stale host session is rejected", category: "Networking")]
	public static UnitTestResult StaleHostSessionIsRejected()
	{
		const ulong hostId = 1;
		ulong previousHostId = MultiplayerSession.HostUserID;
		bool previousIsHost = MultiplayerSession.IsHost;
		var previousPlayers = new Dictionary<ulong, MultiplayerPlayer>(MultiplayerSession.ConnectedPlayers);

		try
		{
			MultiplayerSession.HostUserID = hostId;
			MultiplayerSession.IsHost = false;
			MultiplayerSession.ConnectedPlayers.Clear();
			var host = new MultiplayerPlayer(hostId);
			long generation = host.BeginConnection(new object());
			MultiplayerSession.ConnectedPlayers.Add(hostId, host);
			PacketHandler.SetClientSessionEpoch(7);
			var packet = new DedicatedServerMessagePacket();

			if (!PacketHandler.CanDispatchPacket(
				    packet, new DispatchContext(hostId, true, generation, 7), localIsHost: false))
				return UnitTestResult.Fail("Current host generation and session epoch were rejected");
			if (PacketHandler.CanDispatchPacket(
				    packet, new DispatchContext(hostId, true, generation + 1, 7), localIsHost: false))
				return UnitTestResult.Fail("Stale host connection generation was accepted by the client");
			if (PacketHandler.CanDispatchPacket(
				    packet, new DispatchContext(hostId, true, generation, 6), localIsHost: false))
				return UnitTestResult.Fail("Stale LAN session epoch was accepted by the client");
			PacketHandler.SetClientSessionEpoch(0);
			if (PacketHandler.IsCurrentDispatchContext(
				    new DispatchContext(hostId, true, generation, 7)))
				return UnitTestResult.Fail("Ended LAN session remained current during reconnect teardown");

			return UnitTestResult.Pass("Client host authority is bound to generation and LAN session epoch");
		}
		finally
		{
			PacketHandler.SetClientSessionEpoch(0);
			MultiplayerSession.ConnectedPlayers.Clear();
			foreach (var pair in previousPlayers)
				MultiplayerSession.ConnectedPlayers.Add(pair.Key, pair.Value);
			MultiplayerSession.HostUserID = previousHostId;
			MultiplayerSession.IsHost = previousIsHost;
		}
	}

	[UnitTest(name: "LAN client: connection epochs are monotonic", category: "Networking")]
	public static UnitTestResult ConnectionEpochsAreMonotonic()
	{
		long first = RiptideClient.BeginConnectionEpoch();
		long second = RiptideClient.BeginConnectionEpoch();
		bool staleAccepted = RiptideClient.IsCurrentConnectionEpoch(first);
		bool currentAccepted = RiptideClient.IsCurrentConnectionEpoch(second);
		RiptideClient.EndConnectionEpoch(second);

		if (second <= first)
			return UnitTestResult.Fail("LAN connection epoch did not increase");
		if (staleAccepted || !currentAccepted)
			return UnitTestResult.Fail("LAN connection epoch did not reject the prior session");

		return UnitTestResult.Pass("Every LAN connection gets a newer epoch and invalidates the prior session");
	}

	[UnitTest(name: "LAN client: new epoch clears peer membership", category: "Networking")]
	public static UnitTestResult NewEpochClearsPeerMembership()
	{
		var client = new RiptideClient();
		client.AddClientToList(2);
		client.AddClientToList(3);
		client.ResetClientMembership();

		return client.ClientList.Count == 0
			? UnitTestResult.Pass("New LAN connection epoch starts without stale peers")
			: UnitTestResult.Fail("New LAN connection epoch retained stale peer IDs");
	}
}
