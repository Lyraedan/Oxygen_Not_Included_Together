using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.Handshake;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.Packets.World.Handlers;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport.Lan;
using Shared;

namespace ONI_Together.DebugTools.UnitTests
{
    public static class NetworkingTests
    {
		[UnitTest(name: "Connection recovery selects retry and snapshot paths", category: "Networking")]
		public static UnitTestResult ConnectionRecoveryDecisions()
		{
			if (!GameClient.ShouldRetryConnection(true, ClientState.Disconnected, 0)
			    || !GameClient.ShouldRetryConnection(true, ClientState.Disconnected, 4)
			    || GameClient.ShouldRetryConnection(false, ClientState.Disconnected, 0)
			    || GameClient.ShouldRetryConnection(true, ClientState.LoadingWorld, 0)
			    || GameClient.ShouldRetryConnection(true, ClientState.Disconnected, 5))
				return UnitTestResult.Fail("Reconnect retry boundary is incorrect");
			if (!GameClient.ShouldRequestSnapshotAfterHandshake(true, 0)
			    || GameClient.ShouldRequestSnapshotAfterHandshake(true, 77)
			    || GameClient.ShouldRequestSnapshotAfterHandshake(false, 0))
				return UnitTestResult.Fail("Reconnect did not distinguish fresh snapshot from loading proof");

			return UnitTestResult.Pass("In-world reconnect retries and reloads an authenticated fresh snapshot");
		}

		[UnitTest(name: "Reconnect proof never resumes unknown tokens", category: "Networking")]
		public static UnitTestResult UnknownReconnectProofFallsBackSafely()
		{
			if (GameStateRequestPacket.EvaluateReconnectProof(
				    isSteam: true, reconnectToken: 0, proofStatus: ReconnectProofStatus.Missing)
			    != GameStateRequestPacket.ReconnectProofDecision.FreshSnapshot
			    || GameStateRequestPacket.EvaluateReconnectProof(
				    isSteam: true, reconnectToken: 77, proofStatus: ReconnectProofStatus.Active)
			    != GameStateRequestPacket.ReconnectProofDecision.ResumeLoading
			    || GameStateRequestPacket.EvaluateReconnectProof(
				    isSteam: true, reconnectToken: 77, proofStatus: ReconnectProofStatus.Missing)
			    != GameStateRequestPacket.ReconnectProofDecision.FreshSnapshot
				    || GameStateRequestPacket.EvaluateReconnectProof(
					    isSteam: false, reconnectToken: 77, proofStatus: ReconnectProofStatus.Missing)
				    != GameStateRequestPacket.ReconnectProofDecision.FreshSnapshot)
					return UnitTestResult.Fail("Unknown token resumed loading or failed to request a fresh snapshot");
			if (GameStateRequestPacket.EvaluateReconnectProof(
				    isSteam: false, reconnectToken: 77, proofStatus: ReconnectProofStatus.Completed)
			    != GameStateRequestPacket.ReconnectProofDecision.FreshSnapshot)
				return UnitTestResult.Fail("Completed LAN proof did not authenticate a fresh snapshot fallback");

			if (!GameClient.ShouldAcceptHostReconnectDecision(77, 77)
			    || !GameClient.ShouldAcceptHostReconnectDecision(77, 0)
			    || GameClient.ShouldAcceptHostReconnectDecision(77, 88)
			    || GameClient.ShouldAcceptHostReconnectDecision(0, 88))
				return UnitTestResult.Fail("Client accepted a forged host reconnect decision");

			return UnitTestResult.Pass("Only active proof resumes; stale and unknown proof request a fresh snapshot");
		}

		[UnitTest(name: "LAN port reserves the adjacent TCP transfer port", category: "Networking")]
		public static UnitTestResult LanPortRange()
		{
			if (!NetworkConfig.IsValidLanPort(1) || !NetworkConfig.IsValidLanPort(65534)
			    || NetworkConfig.IsValidLanPort(0) || NetworkConfig.IsValidLanPort(65535)
			    || NetworkConfig.IsValidLanPort(-1) || NetworkConfig.IsValidLanPort(70000))
				return UnitTestResult.Fail("LAN port validation does not reserve port + 1 for TCP");

			return UnitTestResult.Pass("LAN UDP port is bounded to 1..65534");
		}

        [UnitTest(name: "Server is running", category: "Networking", liveSafe: true)]
        public static UnitTestResult ServerStarts()
        {
            if (!MultiplayerSession.InSession)
                return UnitTestResult.Skip("Requires an active multiplayer session");

            if (NetworkConfig.TransportServer == null)
                return UnitTestResult.Fail("TransportServer is null");

            return UnitTestResult.Pass("Server is running");
        }

        [UnitTest(name: "Using Steamworks Transport", category: "Networking", liveSafe: true)]
        public static UnitTestResult IsSteamTransport()
        {
            if (NetworkConfig.transport != NetworkConfig.NetworkTransport.STEAMWORKS)
                return UnitTestResult.Skip("Steamworks transport is not selected");
            return UnitTestResult.Pass("Transport is Steamworks");
        }

        [UnitTest(name: "Using Riptide Transport", category: "Networking", liveSafe: true)]
        public static UnitTestResult IsRiptideTransport()
        {
            if (NetworkConfig.transport != NetworkConfig.NetworkTransport.RIPTIDE)
                return UnitTestResult.Skip("Riptide transport is not selected");
            return UnitTestResult.Pass("Transport is Riptide");
        }

        [UnitTest(name: "Check for duplicate network identities", category: "Networking", liveSafe: true)]
        public static UnitTestResult CheckForDuplicateNetworkIdentities()
        {
            var identities = NetworkIdentityRegistry.AllIdentities;
            foreach(var identity in identities)
            {
                int id = identity.NetId;
                var matches = identities.Where(x => x.NetId == id).ToList();
                if (matches.Count > 1)
                    return UnitTestResult.Fail($"NetId {identity.NetId} has {matches.Count} identities");
            }
            return UnitTestResult.Pass("No duplicate network identities found");
        }

        [UnitTest(name: "TCP file transfer server ready (host, LAN)", category: "Networking", liveSafe: true)]
        public static UnitTestResult TcpTransferServerReady()
        {
            if (!MultiplayerSession.IsHost)
                return UnitTestResult.Skip("Requires a multiplayer host");

            if (!NetworkConfig.IsLanConfig())
                return UnitTestResult.Skip("Requires Riptide/LAN transport");

            if (NetworkConfig.TransportServer is not RiptideServer server)
                return UnitTestResult.Fail("TransportServer is not a RiptideServer");

            if (server.TcpTransfer == null)
                return UnitTestResult.Fail("TcpFileTransfer is null (listener failed to start, UDP fallback in use)");

            int riptidePort = Configuration.Instance.Host.LanSettings.Port;
            return UnitTestResult.Pass($"TcpFileTransferServer running on port {riptidePort + 1}");
        }

        [UnitTest(name: "UDP save-transfer fallback pipeline registered", category: "Networking", liveSafe: true)]
        public static UnitTestResult UdpFallbackAvailable()
        {
            if (!PacketRegistry.HasRegisteredPacket(typeof(SaveFileRequestPacket)))
                return UnitTestResult.Fail("SaveFileRequestPacket not registered");

            if (!PacketRegistry.HasRegisteredPacket(typeof(SaveFileChunkPacket)))
                return UnitTestResult.Fail("SaveFileChunkPacket not registered");

            return UnitTestResult.Pass("Save-transfer UDP fallback packets are registered");
        }

        [UnitTest(name: "Gantry toggle receive handler registered", category: "Networking")]
        public static UnitTestResult GantryToggleHandlerRegistered()
        {
            var handler = new MiscBuildingHandler();
            if (!handler.SupportedConfigHashes.Contains(NetworkingHash.ForConfigKey("GantryToggle")))
                return UnitTestResult.Fail("GantryToggle is missing from the building config receive handler");

            return UnitTestResult.Pass("GantryToggle receive handler is registered");
        }

        [UnitTest(name: "Auto chunking: split and reassemble roundtrip", category: "Networking")]
        public static UnitTestResult AutoChunkingWorks()
        {
            const int payloadSize = 2500;
            const int chunkSize = 900;

            byte[] payload = new byte[payloadSize];
            var rnd = new Random(42);
            rnd.NextBytes(payload);

            int totalChunks = (payloadSize + chunkSize - 1) / chunkSize;
            int sequence = ChunkedPacket.GetNextSequenceId();

            var roundtripped = new List<byte[]>(totalChunks);
            for (int i = 0; i < totalChunks; i++)
            {
                int offset = i * chunkSize;
                int length = Math.Min(chunkSize, payloadSize - offset);
                byte[] slice = new byte[length];
                Array.Copy(payload, offset, slice, 0, length);

                var chunk = new ChunkedPacket
                {
                    SequenceId = sequence,
                    ChunkIndex = i,
                    TotalChunks = totalChunks,
                    ChunkData = slice
                };

                using var ms = new MemoryStream();
                using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
                    chunk.Serialize(w);
                ms.Position = 0;
                var copy = new ChunkedPacket();
                using (var r = new BinaryReader(ms, Encoding.UTF8, true))
                    copy.Deserialize(r);

                if (copy.SequenceId != sequence || copy.ChunkIndex != i || copy.TotalChunks != totalChunks)
                    return UnitTestResult.Fail($"Chunk {i} header did not roundtrip");

                if (copy.ChunkData.Length != length)
                    return UnitTestResult.Fail($"Chunk {i} data length mismatch: got {copy.ChunkData.Length}, expected {length}");

                roundtripped.Add(copy.ChunkData);
            }

            byte[] reassembled = new byte[payloadSize];
            int writeOffset = 0;
            foreach (var part in roundtripped)
            {
                Array.Copy(part, 0, reassembled, writeOffset, part.Length);
                writeOffset += part.Length;
            }

            if (writeOffset != payloadSize)
                return UnitTestResult.Fail($"Reassembled size {writeOffset} != original {payloadSize}");

            for (int i = 0; i < payloadSize; i++)
            {
                if (reassembled[i] != payload[i])
                    return UnitTestResult.Fail($"Reassembled byte {i} differs from original");
            }

            return UnitTestResult.Pass($"Chunked {payloadSize} bytes into {totalChunks} chunks and reassembled byte-identical");
        }

        [UnitTest(name: "All expected clients connected", category: "Networking", liveSafe: true)]
        public static UnitTestResult AllClientsConnected()
        {
            if (!MultiplayerSession.InSession)
                return UnitTestResult.Skip("Requires an active multiplayer session");

            var transportClients = NetworkConfig.GetConnectedClients();
            if (transportClients.Count == 0)
                return UnitTestResult.Fail("Transport reports zero connected clients");

			int expectedRemoteClients = 0;
            foreach (var clientId in transportClients)
            {
				if (clientId == MultiplayerSession.LocalUserID)
					continue;
				if (MultiplayerSession.IsClient && clientId != MultiplayerSession.HostUserID)
					continue;

				expectedRemoteClients++;
				if (!MultiplayerSession.ConnectedPlayers.TryGetValue(clientId, out MultiplayerPlayer player)
				    || player.Connection == null)
                    return UnitTestResult.Fail($"Transport client {clientId} is missing from ConnectedPlayers");
            }

			return UnitTestResult.Pass($"{expectedRemoteClients} remote clients connected and tracked in session");
        }

        [UnitTest(name: "Packet routing: host never sends to itself", category: "Networking", liveSafe: true)]
        public static UnitTestResult PacketRouting()
        {
            if (!MultiplayerSession.InSession)
                return UnitTestResult.Skip("Requires an active multiplayer session");

            if (!MultiplayerSession.HostUserID.IsValid())
                return UnitTestResult.Fail("HostUserID is not valid");

            if (MultiplayerSession.IsHost && MultiplayerSession.LocalUserID != MultiplayerSession.HostUserID)
                return UnitTestResult.Fail($"IsHost but LocalUserID {MultiplayerSession.LocalUserID} != HostUserID {MultiplayerSession.HostUserID}");

            foreach (var kvp in MultiplayerSession.ConnectedPlayers)
            {
                var player = kvp.Value;
                if (player == null)
                    return UnitTestResult.Fail($"Player {kvp.Key} entry is null");
                if (player.PlayerId != kvp.Key)
                    return UnitTestResult.Fail($"ConnectedPlayers key {kvp.Key} != PlayerId {player.PlayerId}");
            }

            if (MultiplayerSession.IsClient)
            {
                if (MultiplayerSession.ConnectedPlayers.ContainsKey(MultiplayerSession.LocalUserID))
                    return UnitTestResult.Fail("Client's own LocalUserID is listed in ConnectedPlayers (only host should be there)");
            }

            return UnitTestResult.Pass("Session routing state is consistent; host self-send guard can function");
        }

		[UnitTest(name: "Mod fingerprint: hashes bytes, config, and load order", category: "Networking")]
		public static UnitTestResult ModFingerprintBindsDeterministicInputs()
		{
			string root = Path.Combine(Path.GetTempPath(), "oni-mod-fingerprint-" + Guid.NewGuid().ToString("N"));
			string content = Path.Combine(root, "content");
			string config = Path.Combine(root, "config");
			try
			{
				Directory.CreateDirectory(content);
				Directory.CreateDirectory(config);
				File.WriteAllText(Path.Combine(content, "code.dll"), "build-a");
				File.WriteAllText(Path.Combine(config, "settings.json"), "{\"rate\":1}");
				string contentA = ProtocolCompatibility.ComputeCanonicalDirectoryHash(content);
				string configA = ProtocolCompatibility.ComputeCanonicalDirectoryHash(config);
				string baseline = ProtocolCompatibility.ComposeModFingerprint(
					0, "mod", "steam", "1", "7", contentA, configA);

				File.WriteAllText(Path.Combine(content, "code.dll"), "build-b");
				string changedContent = ProtocolCompatibility.ComposeModFingerprint(
					0, "mod", "steam", "1", "7",
					ProtocolCompatibility.ComputeCanonicalDirectoryHash(content), configA);
				File.WriteAllText(Path.Combine(content, "code.dll"), "build-a");
				File.WriteAllText(Path.Combine(config, "settings.json"), "{\"rate\":2}");
				string changedConfig = ProtocolCompatibility.ComposeModFingerprint(
					0, "mod", "steam", "1", "7", contentA,
					ProtocolCompatibility.ComputeCanonicalDirectoryHash(config));
				string changedOrder = ProtocolCompatibility.ComposeModFingerprint(
					1, "mod", "steam", "1", "7", contentA, configA);

				if (baseline == changedContent || baseline == changedConfig || baseline == changedOrder)
					return UnitTestResult.Fail("Mod fingerprint ignored content, configuration, or load order");
				return UnitTestResult.Pass("Mod fingerprint binds content, deterministic config, and load order");
			}
			finally
			{
				if (Directory.Exists(root))
					Directory.Delete(root, true);
			}
		}

		[UnitTest(name: "Host-ordered commands: stale revisions are rejected", category: "Networking")]
		public static UnitTestResult HostOrderedCommandsRejectStaleRevisions()
		{
			SpeedChangePacket.ResetSessionState();
			RedAlertStatePacket.ResetSessionState();
			try
			{
				if (!SpeedChangePacket.TryAcceptAuthoritativeRevision(2)
				    || SpeedChangePacket.TryAcceptAuthoritativeRevision(2)
				    || SpeedChangePacket.TryAcceptAuthoritativeRevision(1))
					return UnitTestResult.Fail("Speed command accepted a duplicate or stale host revision");
				if (!RedAlertStatePacket.TryAcceptAuthoritativeRevision(7, 3)
				    || RedAlertStatePacket.TryAcceptAuthoritativeRevision(7, 2)
				    || !RedAlertStatePacket.TryAcceptAuthoritativeRevision(8, 1))
					return UnitTestResult.Fail("Red-alert revisions were not monotonic per world");
				return UnitTestResult.Pass("Speed and per-world red alert reject stale host revisions");
			}
			finally
			{
				SpeedChangePacket.ResetSessionState();
				RedAlertStatePacket.ResetSessionState();
			}
		}

		[UnitTest(name: "Mod API: authority is explicit and defaults host-only", category: "Networking")]
		public static UnitTestResult ModApiAuthorityDefaultsHostOnly()
		{
			if (PacketRegistry.DefaultModApiAuthority != ModApiAuthority.HostToClientsOnly)
				return UnitTestResult.Fail("Legacy Mod API registration no longer defaults host-only");
			if (PacketRegistry.CanClientDispatchModApi(ModApiAuthority.HostToClientsOnly, false)
			    || PacketRegistry.CanClientDispatchModApi(ModApiAuthority.HostToClientsOnly, true))
				return UnitTestResult.Fail("Default Mod API packet accepted a ready client origin");
			if (!PacketRegistry.CanClientDispatchModApi(ModApiAuthority.ClientToHost, false)
			    || PacketRegistry.CanClientDispatchModApi(ModApiAuthority.ClientToHost, true)
			    || !PacketRegistry.CanClientDispatchModApi(ModApiAuthority.ClientBroadcast, true)
			    || PacketRegistry.CanClientDispatchModApi(ModApiAuthority.ClientBroadcast, false))
				return UnitTestResult.Fail("Explicit Mod API client policy was not enforced");
			return UnitTestResult.Pass("Mod API defaults host-only and honors explicit client authority");
		}

		[UnitTest(name: "Host-ordered command owns authoritative fanout", category: "Networking")]
		public static UnitTestResult HostOrderedCommandOwnsFanout()
		{
			int fanoutCount = 0;
			bool dispatched = HostBroadcastPacket.DispatchVerifiedRelayAndFanOut(
				new SpeedChangePacket(SpeedChangePacket.SpeedState.Normal),
				new DispatchContext(101, false),
				(_, _) => true,
				(_, _) => fanoutCount++);
			return dispatched && fanoutCount == 0
				? UnitTestResult.Pass("Host-ordered command bypasses generic sender-excluding fanout")
				: UnitTestResult.Fail("Host-ordered command was generically fanned out");
		}

		[UnitTest(name: "Host-ordered commands: invalid wire inputs are rejected", category: "Networking")]
		public static UnitTestResult HostOrderedCommandsRejectInvalidWireInputs()
		{
			if (!InvalidSpeedThrows())
				return UnitTestResult.Fail("Invalid speed enum was accepted");
			if (!InvalidWorldThrows())
				return UnitTestResult.Fail("Negative world id was accepted");
			return UnitTestResult.Pass("Invalid speed enums and world ids are rejected at deserialize");
		}

		private static bool InvalidSpeedThrows()
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
			{
				writer.Write(99);
				writer.Write(0L);
			}
			stream.Position = 0;
			try
			{
				using var reader = new BinaryReader(stream);
				new SpeedChangePacket().Deserialize(reader);
				return false;
			}
			catch (InvalidDataException)
			{
				return true;
			}
		}

		private static bool InvalidWorldThrows()
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
			{
				writer.Write(-1);
				writer.Write(false);
				writer.Write(0L);
			}
			stream.Position = 0;
			try
			{
				using var reader = new BinaryReader(stream);
				new RedAlertStatePacket().Deserialize(reader);
				return false;
			}
			catch (InvalidDataException)
			{
				return true;
			}
		}

    }
}
