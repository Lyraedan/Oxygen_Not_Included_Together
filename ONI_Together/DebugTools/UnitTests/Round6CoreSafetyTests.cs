using System;
using System.IO;
using System.Threading;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Animation;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.Handshake;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transfer;
using ONI_Together.Networking.Transport.Steam;
using ONI_Together.Networking.Transport.Steamworks;
using Steamworks;

namespace ONI_Together.DebugTools.UnitTests;

public static class Round6CoreSafetyTests
{
	private static long _requestTestGeneration;
	[UnitTest(name: "Protocol gate: handshake, verified, and ready phases", category: "Networking")]
	public static UnitTestResult ProtocolAndReadyPhases()
	{
		var handshake = new GameStateRequestPacket();
		var control = new SaveFileRequestPacket();
		var animRepair = new AnimResyncRequestPacket();
		var positionRepair = new EntityPositionRequestPacket();
		var mutation = new HostBroadcastPacket();

		if (!PacketHandler.CanDispatchClientPacket(handshake, protocolVerified: false, ClientReadyState.Unready))
			return UnitTestResult.Fail("Unverified handshake was rejected");
		if (PacketHandler.CanDispatchClientPacket(control, protocolVerified: false, ClientReadyState.Unready))
			return UnitTestResult.Fail("Unverified client control packet was accepted");
		if (!PacketHandler.CanDispatchClientPacket(control, protocolVerified: true, ClientReadyState.Unready))
			return UnitTestResult.Fail("Verified pre-ready control packet was rejected");
		if (GameClient.CanSendRuntimeRequests(ClientState.LoadingWorld)
		    || GameClient.CanSendRuntimeRequests(ClientState.Connected)
		    || GameClient.CanSendRuntimeRequests(ClientState.AwaitingReadyAck)
		    || !GameClient.CanSendRuntimeRequests(ClientState.InGame))
			return UnitTestResult.Fail("Runtime request sender gate did not require InGame state");
		if (PacketSender.CanSendPeerRuntime(isHost: false, ClientState.LoadingWorld)
		    || PacketSender.CanSendPeerRuntime(isHost: false, ClientState.AwaitingReadyAck)
		    || !PacketSender.CanSendPeerRuntime(isHost: false, ClientState.InGame)
		    || !PacketSender.CanSendPeerRuntime(isHost: true, ClientState.LoadingWorld))
			return UnitTestResult.Fail("Peer runtime sender gate did not preserve host authority and require client InGame state");
		if (!PacketHandler.CanSendClientPacket(handshake, ClientState.Connected)
		    || !PacketHandler.CanSendClientPacket(control, ClientState.LoadingWorld)
		    || !PacketHandler.CanSendClientPacket(new ReadyAcceptedAckPacket(), ClientState.AwaitingReadyAck)
		    || PacketHandler.CanSendClientPacket(mutation, ClientState.LoadingWorld)
		    || !PacketHandler.CanSendClientPacket(mutation, ClientState.InGame))
			return UnitTestResult.Fail("Direct host sender gate did not separate reconnect control from runtime mutation");
		if (PacketHandler.CanDispatchClientPacket(animRepair, protocolVerified: true, ClientReadyState.Loading)
		    || PacketHandler.CanDispatchClientPacket(positionRepair, protocolVerified: true, ClientReadyState.Loading))
			return UnitTestResult.Fail("Host accepted runtime repair before Ready");
		if (PacketHandler.CanDispatchClientPacket(animRepair, protocolVerified: false, ClientReadyState.Loading)
		    || PacketHandler.CanDispatchClientPacket(positionRepair, protocolVerified: false, ClientReadyState.Loading))
			return UnitTestResult.Fail("Unverified client could request loading-state repair");
		if (!PacketHandler.CanDispatchClientPacket(animRepair, protocolVerified: true, ClientReadyState.Ready)
		    || !PacketHandler.CanDispatchClientPacket(positionRepair, protocolVerified: true, ClientReadyState.Ready))
			return UnitTestResult.Fail("Ready client could not request runtime state repair");
		if (PacketHandler.CanDispatchClientPacket(mutation, protocolVerified: true, ClientReadyState.Unready))
			return UnitTestResult.Fail("Unready state mutation was accepted");
		if (!PacketHandler.CanDispatchClientPacket(mutation, protocolVerified: true, ClientReadyState.Ready))
			return UnitTestResult.Fail("Ready state mutation was rejected");

		return UnitTestResult.Pass("Client packets advance through handshake, verified, and ready phases");
	}

	[UnitTest(name: "Viewport runtime requires verified exact-ready recipient", category: "Networking")]
	public static UnitTestResult ViewportRuntimeRequiresExactReady()
	{
		var player = new MultiplayerPlayer(919);
		player.BeginConnection(new object());
		if (WorldStateSyncer.CanReceiveViewportRuntime(player))
			return UnitTestResult.Fail("Unverified reconnect recipient received viewport runtime");

		player.ProtocolVerified = true;
		player.readyState = ClientReadyState.Loading;
		if (WorldStateSyncer.CanReceiveViewportRuntime(player))
			return UnitTestResult.Fail("Loading reconnect recipient received viewport runtime");

		player.readyState = ClientReadyState.Ready;
		if (!WorldStateSyncer.CanReceiveViewportRuntime(player))
			return UnitTestResult.Fail("Verified exact-ready recipient was excluded from viewport runtime");

		return UnitTestResult.Pass("Viewport runtime is gated by connection, protocol, and exact Ready");
	}

	[UnitTest(name: "API relay carries the real client sender id", category: "Networking")]
	public static UnitTestResult ApiRelayCarriesClientSender()
	{
		const ulong senderId = 920;
		HostBroadcastPacket relay = PacketSender.CreateHostRelayForClient(
			new EntityPositionRequestPacket(), senderId);
		if (relay.SenderId != senderId)
			return UnitTestResult.Fail("Client relay replaced its transport identity");

		return UnitTestResult.Pass("Client relay wire sender matches the transport identity");
	}

	[UnitTest(name: "Connection generation: reconnect resets authority", category: "Networking")]
	public static UnitTestResult ReconnectResetsAuthority()
	{
		var player = new MultiplayerPlayer(901);
		object first = new object();
		long firstGeneration = player.BeginConnection(first);
		player.ProtocolVerified = true;
		player.readyState = ClientReadyState.Ready;

		object second = new object();
		long secondGeneration = player.BeginConnection(second);
		if (secondGeneration <= firstGeneration || player.ProtocolVerified
		    || player.readyState != ClientReadyState.Unready)
			return UnitTestResult.Fail("Reconnect retained protocol or ready authority");
		if (player.IsCurrentConnection(first, firstGeneration)
		    || !player.IsCurrentConnection(second, secondGeneration))
			return UnitTestResult.Fail("Connection generation did not reject stale traffic");
		if (player.EndConnection(first, firstGeneration) || player.Connection == null)
			return UnitTestResult.Fail("Stale close reset the current connection");
		if (!player.EndConnection(second, secondGeneration) || player.Connection != null
		    || player.ProtocolVerified || player.readyState != ClientReadyState.Unready)
			return UnitTestResult.Fail("Current close did not reset authority");

		return UnitTestResult.Pass("Reconnect and disconnect reset authority by connection generation");
	}

	[UnitTest(name: "Steam client: stale host connection is rejected", category: "Networking")]
	public static UnitTestResult SteamClientRejectsStaleHostConnection()
	{
		const ulong hostId = 906;
		ulong previousHostId = MultiplayerSession.HostUserID;
		bool previousIsHost = MultiplayerSession.IsHost;
		MultiplayerSession.ConnectedPlayers.TryGetValue(hostId, out MultiplayerPlayer previousPlayer);
		var first = new HSteamNetConnection { m_HSteamNetConnection = 6001 };
		var second = new HSteamNetConnection { m_HSteamNetConnection = 6002 };

		try
		{
			MultiplayerSession.HostUserID = hostId;
			MultiplayerSession.IsHost = false;
			var host = new MultiplayerPlayer(hostId);
			MultiplayerSession.ConnectedPlayers[hostId] = host;
			long firstGeneration = host.BeginConnection(first);
			if (!SteamworksClient.TryCreateHostDispatchContext(first, hostId, out var firstContext)
			    || firstContext.ConnectionGeneration != firstGeneration)
				return UnitTestResult.Fail("Current Steam host connection had no generation-bound context");

			long secondGeneration = host.BeginConnection(second);
			if (SteamworksClient.TryCreateHostDispatchContext(first, hostId, out _)
			    || !SteamworksClient.TryCreateHostDispatchContext(second, hostId, out var secondContext)
			    || secondContext.ConnectionGeneration != secondGeneration)
				return UnitTestResult.Fail("Steam host reconnect accepted stale traffic");

			return UnitTestResult.Pass("Steam client dispatch is bound to the current host generation");
		}
		finally
		{
			if (previousPlayer == null)
				MultiplayerSession.ConnectedPlayers.Remove(hostId);
			else
				MultiplayerSession.ConnectedPlayers[hostId] = previousPlayer;
			MultiplayerSession.HostUserID = previousHostId;
			MultiplayerSession.IsHost = previousIsHost;
		}
	}

	[UnitTest(name: "Steam server: stale close preserves current snapshot", category: "Networking")]
	public static UnitTestResult SteamServerStaleClosePreservesCurrentSnapshot()
	{
		const ulong clientId = 907;
		const string transferId = "stale-close";
		MultiplayerSession.ConnectedPlayers.TryGetValue(clientId, out MultiplayerPlayer previousPlayer);
		object first = new object();
		object second = new object();
		var player = new MultiplayerPlayer(clientId);
		MultiplayerSession.ConnectedPlayers[clientId] = player;

		try
		{
			player.BeginConnection(first);
			long secondGeneration = player.BeginConnection(second);
			if (!player.TryBeginSaveTransfer(out long transferGeneration))
				return UnitTestResult.Fail("Current Steam connection could not begin snapshot transfer");
			SaveFileTransferManager.StartTransfer(clientId, transferId, 2);

			if (SteamworksServer.TryCleanupClientSession(clientId, first)
			    || !player.IsCurrentConnection(second, secondGeneration)
			    || !player.IsCurrentSaveTransfer(transferGeneration)
			    || SaveFileTransferManager.GetChunkSendDecision(clientId, transferId, 0)
			       != SaveFileTransferManager.ChunkSendDecision.Send)
				return UnitTestResult.Fail("Stale Steam close cancelled the current snapshot");

			if (!SteamworksServer.TryCleanupClientSession(clientId, second)
			    || player.Connection != null
			    || SaveFileTransferManager.GetChunkSendDecision(clientId, transferId, 0)
			       != SaveFileTransferManager.ChunkSendDecision.Stop)
				return UnitTestResult.Fail("Current Steam close did not clean up its snapshot");

			return UnitTestResult.Pass("Only the current Steam connection can cancel its snapshot");
		}
		finally
		{
			SaveFileTransferManager.CancelTransfers(clientId);
			if (previousPlayer == null)
				MultiplayerSession.ConnectedPlayers.Remove(clientId);
			else
				MultiplayerSession.ConnectedPlayers[clientId] = previousPlayer;
		}
	}

	[UnitTest(name: "Save transfer: request and fallback are one-shot", category: "Networking")]
	public static UnitTestResult SaveTransferIsOneShot()
	{
		var player = new MultiplayerPlayer(902);
		player.BeginConnection(new object());
		if (!player.TryBeginSaveTransfer(out long generation)
		    || player.TryBeginSaveTransfer(out _))
			return UnitTestResult.Fail("Duplicate save request was accepted");
		if (!player.TrySetSaveTransferToken(generation, "token-a")
		    || !player.TryRequestSaveFallback("token-a")
		    || player.TryRequestSaveFallback("token-a")
		    || player.TryRequestSaveFallback("token-b"))
			return UnitTestResult.Fail("TCP fallback was not bound one-shot to its transfer");
		if (!player.TryRestartSaveTransferAfterFallback(out long fallbackGeneration)
		    || fallbackGeneration <= generation
		    || player.TryRestartSaveTransferAfterFallback(out _)
		    || player.TryRequestSaveFallback("token-a"))
			return UnitTestResult.Fail("UDP restart was not one-shot after the authenticated fallback");
		var fallback = new TcpFallbackRequestPacket { Requester = 902, TransferToken = "token-a" };
		using var stream = new MemoryStream();
		using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
			fallback.Serialize(writer);
		stream.Position = 0;
		var fallbackCopy = new TcpFallbackRequestPacket();
		using (var reader = new BinaryReader(stream))
			fallbackCopy.Deserialize(reader);
		if (fallbackCopy.Requester != 902 || fallbackCopy.TransferToken != "token-a")
			return UnitTestResult.Fail("TCP fallback lost its authenticated transfer token");
		player.CompleteSaveTransfer();
		if (!player.TryBeginSaveTransfer(out _))
			return UnitTestResult.Fail("Completed transfer blocked the next sync");

		return UnitTestResult.Pass("Save request and fallback are one-shot per active transfer");
	}

	[UnitTest(name: "Steam lobby departure: active transport owns player lifetime", category: "Networking")]
	public static UnitTestResult SteamLobbyDeparturePreservesActiveTransport()
	{
		const ulong playerId = 904;
		object connection = new object();
		var player = new MultiplayerPlayer(playerId);
		MultiplayerSession.ConnectedPlayers[playerId] = player;

		try
		{
			long connectionGeneration = player.BeginConnection(connection);
			player.ProtocolVerified = true;
			if (!player.TryBeginSaveTransfer(out long transferGeneration))
				return UnitTestResult.Fail("Active transport could not start a save transfer");

			bool removedWhileConnected = SteamLobby.RemoveLobbyMemberIfTransportClosed(playerId);
			if (removedWhileConnected
			    || !MultiplayerSession.ConnectedPlayers.TryGetValue(playerId, out MultiplayerPlayer current)
			    || !ReferenceEquals(current, player)
			    || !player.IsCurrentConnection(connection, connectionGeneration)
			    || !player.IsCurrentSaveTransfer(transferGeneration))
				return UnitTestResult.Fail("Lobby departure destroyed an established transport session");

			if (!player.EndConnection(connection, connectionGeneration)
			    || !SteamLobby.RemoveLobbyMemberIfTransportClosed(playerId)
			    || MultiplayerSession.ConnectedPlayers.ContainsKey(playerId))
				return UnitTestResult.Fail("Transport close did not release the departed lobby member");

			return UnitTestResult.Pass("Lobby presence cannot invalidate an established transport session");
		}
		finally
		{
			MultiplayerSession.ConnectedPlayers.Remove(playerId);
		}
	}

	[UnitTest(name: "Steam snapshot: chunk size leaves send-buffer headroom", category: "Networking")]
	public static UnitTestResult SteamSnapshotChunkSizeLeavesHeadroom()
	{
		int defaultSteamChunkSize = SaveHelper.ResolveSaveFileChunkSizeKb(
			256, NetworkConfig.NetworkTransport.STEAMWORKS);
		int smallSteamChunkSize = SaveHelper.ResolveSaveFileChunkSizeKb(
			16, NetworkConfig.NetworkTransport.STEAMWORKS);
		int lanChunkSize = SaveHelper.ResolveSaveFileChunkSizeKb(
			256, NetworkConfig.NetworkTransport.RIPTIDE);

		if (defaultSteamChunkSize != 64 || smallSteamChunkSize != 16 || lanChunkSize != 256)
			return UnitTestResult.Fail("Steam snapshot chunks did not preserve send-buffer headroom");

		return UnitTestResult.Pass("Steam snapshot chunks are capped without changing LAN policy");
	}

	[UnitTest(name: "Steam snapshot: ACK window is bounded and idempotent", category: "Networking")]
	public static UnitTestResult SteamSnapshotAckWindowIsBounded()
	{
		const ulong clientId = 905;
		const string transferId = "ack-window";
		int window = SaveFileTransferManager.AckWindowChunks;
		int totalChunks = window + 2;

		SaveFileTransferManager.ResetSessionState();
		try
		{
			SaveFileTransferManager.StartTransfer(clientId, transferId, totalChunks);
			SaveFileTransferManager.HandleChunkAck(clientId, transferId, 0);

			for (int chunkIndex = 0; chunkIndex < window; chunkIndex++)
			{
				if (SaveFileTransferManager.GetChunkSendDecision(clientId, transferId, chunkIndex)
				    != SaveFileTransferManager.ChunkSendDecision.Send)
					return UnitTestResult.Fail("Initial ACK window did not permit sequential chunks");
				SaveFileTransferManager.MarkChunkSent(clientId, transferId, chunkIndex);
			}

			if (SaveFileTransferManager.GetChunkSendDecision(clientId, transferId, window)
			    != SaveFileTransferManager.ChunkSendDecision.Wait)
				return UnitTestResult.Fail("Unsent ACK or full window released extra capacity");

			SaveFileTransferManager.HandleChunkAck(clientId, transferId, window - 1);
			SaveFileTransferManager.HandleChunkAck(clientId, transferId, window - 1);
			SaveFileTransferManager.HandleChunkAck(clientId + 1, transferId, 1);
			SaveFileTransferManager.HandleChunkAck(clientId, "unknown", 1);
			if (SaveFileTransferManager.GetChunkSendDecision(clientId, transferId, window)
			    != SaveFileTransferManager.ChunkSendDecision.Wait)
				return UnitTestResult.Fail("Out-of-order, duplicate, or foreign ACK slid the window");

			SaveFileTransferManager.HandleChunkAck(clientId, transferId, 0);
			if (SaveFileTransferManager.GetChunkSendDecision(clientId, transferId, window)
			    != SaveFileTransferManager.ChunkSendDecision.Send)
				return UnitTestResult.Fail("First contiguous ACK did not release one chunk");
			SaveFileTransferManager.MarkChunkSent(clientId, transferId, window);

			if (SaveFileTransferManager.GetChunkSendDecision(clientId, transferId, window + 1)
			    != SaveFileTransferManager.ChunkSendDecision.Wait)
				return UnitTestResult.Fail("Window exceeded its bound after one release");

			SaveFileTransferManager.HandleChunkAck(clientId, transferId, 1);
			if (SaveFileTransferManager.GetChunkSendDecision(clientId, transferId, window + 1)
			    != SaveFileTransferManager.ChunkSendDecision.Send)
				return UnitTestResult.Fail("Second contiguous ACK did not advance the window");

			return UnitTestResult.Pass("Only contiguous, valid ACKs advance the bounded send window");
		}
		finally
		{
			SaveFileTransferManager.ResetSessionState();
		}
	}

	[UnitTest(name: "TCP server deadlines are absolute and operation-bounded", category: "Networking")]
	public static UnitTestResult TcpServerDeadlinesAreAbsolute()
	{
		TimeSpan lifetime = TimeSpan.FromSeconds(10);
		int initial = TcpFileTransferServer.CalculateTimeoutMilliseconds(TimeSpan.Zero, lifetime);
		int nearDeadline = TcpFileTransferServer.CalculateTimeoutMilliseconds(
			TimeSpan.FromMilliseconds(9500), lifetime);
		int expired = TcpFileTransferServer.CalculateTimeoutMilliseconds(lifetime, lifetime);
		if (initial != 10000 || nearDeadline < 1 || nearDeadline > 500 || expired != 0)
			return UnitTestResult.Fail("TCP timeout window reset instead of shrinking to the deadline");

		return UnitTestResult.Pass("TCP handshake and writes have absolute bounded lifetimes");
	}

	[UnitTest(name: "Host broadcast: replay and stale ids are rejected", category: "Networking")]
	public static UnitTestResult HostBroadcastRejectsReplay()
	{
		long generation = Interlocked.Increment(ref _requestTestGeneration);
		if (!HostBroadcastPacket.TryBeginRequest(10, 100, generation)
		    || HostBroadcastPacket.TryBeginRequest(10, 100, generation)
		    || !HostBroadcastPacket.TryBeginRequest(20, 100, generation))
			return UnitTestResult.Fail("Completed request ids were not scoped to sender");
		if (!HostBroadcastPacket.TryBeginRequest(10, 101, generation)
		    || HostBroadcastPacket.TryBeginRequest(10, 1, generation))
			return UnitTestResult.Fail("Freshness window accepted a stale request");

		return UnitTestResult.Pass("Host-broadcast request ids are sender-scoped and replay-safe");
	}

	[UnitTest(name: "Host broadcast: cursor loss cannot reject ordered commands", category: "Networking")]
	public static UnitTestResult CursorLaneIsIndependentFromOrderedCommands()
	{
		long generation = Interlocked.Increment(ref _requestTestGeneration);
		if (!HostBroadcastPacket.TryBeginRequest(
			    10, 102, generation, HostBroadcastPacket.SequenceLane.CursorSnapshot)
		    || !HostBroadcastPacket.TryBeginRequest(
			    10, 101, generation, HostBroadcastPacket.SequenceLane.Ordered)
		    || HostBroadcastPacket.TryBeginRequest(
			    10, 101, generation, HostBroadcastPacket.SequenceLane.CursorSnapshot))
			return UnitTestResult.Fail(
				"A newer cursor datagram rejected an older reliable command or accepted stale cursor state");
		return UnitTestResult.Pass(
			"Cursor snapshots are sequenced independently from ordered command relays");
	}

	[UnitTest(name: "Chunk packet: completed sequence has tombstone", category: "Networking")]
	public static UnitTestResult ChunkCompletionHasTombstone()
	{
		int sequence = ChunkedPacket.GetNextSequenceId();
		var context = new DispatchContext(903, false);
		var chunk = new ChunkedPacket
		{
			SequenceId = sequence,
			ChunkIndex = 0,
			TotalChunks = 1,
			ChunkData = new byte[] { 7 }
		};
		if (!ChunkedPacket.TryAcceptChunk(chunk, context, out _, out _))
			return UnitTestResult.Fail("Initial sequence did not complete");
		if (ChunkedPacket.TryAcceptChunk(chunk, context, out _, out _))
			return UnitTestResult.Fail("Completed sequence was assembled twice");

		return UnitTestResult.Pass("Completed chunk sequence remains tombstoned");
	}

	[UnitTest(name: "TCP callback: session binding rejects stale completion", category: "Networking")]
	public static UnitTestResult TcpCallbackIsSessionBound()
	{
		var expected = new TcpTransferStartPacket.TransferBinding(4, 11, 22);
		if (!TcpTransferStartPacket.IsCurrentTransfer(expected,
			    new TcpTransferStartPacket.TransferBinding(4, 11, 22), true)
		    || TcpTransferStartPacket.IsCurrentTransfer(expected,
			    new TcpTransferStartPacket.TransferBinding(5, 11, 22), true)
		    || TcpTransferStartPacket.IsCurrentTransfer(expected,
			    new TcpTransferStartPacket.TransferBinding(4, 12, 22), true)
		    || TcpTransferStartPacket.IsCurrentTransfer(expected,
			    new TcpTransferStartPacket.TransferBinding(4, 11, 23), true)
		    || TcpTransferStartPacket.IsCurrentTransfer(expected,
			    new TcpTransferStartPacket.TransferBinding(4, 11, 22), false))
			return UnitTestResult.Fail("Stale TCP callback passed its session binding");

		return UnitTestResult.Pass("TCP callback is bound to transfer, host, client, and active session");
	}

	[UnitTest(name: "Storage transfer: signed item ids roundtrip", category: "Networking")]
	public static UnitTestResult SignedStorageItemIdRoundtrips()
	{
		var source = new StorageItemPacket
		{
			NetId = -17,
			StorageNetId = 5,
			FxPrefix = Storage.FXPrefix.Delivered,
			ConsumedAmount = 1f
		};
		using var stream = new MemoryStream();
		using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
			source.Serialize(writer);
		stream.Position = 0;
		var copy = new StorageItemPacket();
		using (var reader = new BinaryReader(stream))
			copy.Deserialize(reader);

		return copy.NetId == -17
			? UnitTestResult.Pass("Signed non-zero item identity is preserved")
			: UnitTestResult.Fail("Signed item identity changed");
	}
}
