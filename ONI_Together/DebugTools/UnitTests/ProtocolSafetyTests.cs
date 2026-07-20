using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Animation;
using ONI_Together.Networking.Packets.Chores;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.DuplicantActions;
using ONI_Together.Networking.Packets.Events;
using ONI_Together.Networking.Packets.Handshake;
using ONI_Together.Networking.Packets.Social;
using ONI_Together.Networking.Packets.Tools;
using ONI_Together.Networking.Packets.Tools.Build;
using ONI_Together.Networking.Packets.Tools.CopySettingsTool;
using ONI_Together.Networking.Packets.Tools.Deconstruct;
using ONI_Together.Networking.Packets.Tools.Dig;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.Packets.World.Buildings;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport;
using Shared;
using Shared.Interfaces.Networking;
using ONI_Together.Networking.Packets.DLC;
using ONI_Together.Misc;

namespace ONI_Together.DebugTools.UnitTests;
public static class ProtocolSafetyTests
{
	private sealed class ThrowingPacketSender : TransportPacketSender
	{
		public override bool SendPacket(
			object conn,
			IPacket packet,
			PacketSendMode sendType = PacketSendMode.ReliableImmediate)
			=> throw new InvalidOperationException("Synthetic transport failure");
	}

	[UnitTest(name: "Cursor relay: both hops use snapshot datagrams", category: "Networking")]
	public static UnitTestResult CursorRelayUsesSnapshotDatagrams()
	{
		if (HostBroadcastPacket.GetRelaySendMode(new PlayerCursorPacket())
		        != PacketSendMode.Unreliable
		    || HostBroadcastPacket.GetRelaySendMode(new PingPacket())
		        != PacketSendMode.Reliable
		    || PacketSender.MAX_PACKET_SIZE_UNRELIABLE != 1000)
			return UnitTestResult.Fail(
				"Cursor snapshots can enter ordered reliable delivery or exceed the LAN datagram budget");
		return UnitTestResult.Pass(
			"Cursor wrapper and host fanout share one unreliable snapshot policy");
	}
	[UnitTest(name: "Direct send: transport exceptions become failures", category: "Networking")]
	public static UnitTestResult DirectSendContainsTransportExceptions()
	{
		TransportPacketSender original = NetworkConfig.TransportPacketSender;
		try
		{
			NetworkConfig.TransportPacketSender = new ThrowingPacketSender();
			if (PacketSender.SendToConnection(new object(), new AllClientsReadyPacket()))
				return UnitTestResult.Fail("Throwing transport was reported as a successful send");

			return UnitTestResult.Pass("Control send failures return false to barrier cleanup paths");
		}
		catch (InvalidOperationException)
		{
			return UnitTestResult.Fail("Transport exception escaped the direct-send boundary");
		}
		finally
		{
			NetworkConfig.TransportPacketSender = original;
		}
	}
	    [UnitTest(name: "Protocol hash: stable fixed vectors", category: "Networking")]
    public static UnitTestResult StableHashVectors()
    {
        const string packetName = "ONI_Together.Networking.Packets.Core.DedicatedServerMessagePacket";
        if (NetworkingHash.ForString(packetName) != 1378864026)
            return UnitTestResult.Fail("DedicatedServerMessagePacket hash vector changed");
        if (NetworkingHash.ForString("hello") != -1169296852)
            return UnitTestResult.Fail("Generic string hash vector changed");
        if (NetworkingHash.ForType(typeof(DedicatedServerMessagePacket)) != 1378864026)
            return UnitTestResult.Fail("Type and string hashes disagree");

        return UnitTestResult.Pass("Stable SHA-256 hash vectors match");
    }

    [UnitTest(name: "Bulk packet: snapshots source queue", category: "Networking")]
    public static UnitTestResult BulkSnapshotsSourceQueue()
    {
        var source = new List<byte[]> { new byte[] { 1, 2, 3 } };
        var packet = new BulkSenderPacket(123, source);
        source.Clear();

        if (packet.SerializedInnerPackets.Count != 1)
            return UnitTestResult.Fail("Clearing the source queue erased the bulk payload");
        if (packet.SerializedInnerPackets[0].Length != 3)
            return UnitTestResult.Fail("Bulk payload was not retained");

        return UnitTestResult.Pass("Bulk packet owns a queue snapshot");
    }

    [UnitTest(name: "Bulk packet: rejects oversized metadata", category: "Networking")]
    public static UnitTestResult BulkRejectsOversizedMetadata()
    {
        if (!DeserializeBulkThrows(BulkSenderPacket.MaxPacketCount + 1, 0))
            return UnitTestResult.Fail("Oversized bulk packet count was accepted");
        if (!DeserializeBulkThrows(1, BulkSenderPacket.MaxInnerPacketBytes + 1))
            return UnitTestResult.Fail("Oversized inner packet length was accepted");

        return UnitTestResult.Pass("Bulk count and length bounds are enforced");
    }

    [UnitTest(name: "Chunk packet: same sequence isolated by sender", category: "Networking")]
    public static UnitTestResult ChunkSequenceIsScopedToSender()
    {
        int sequence = ChunkedPacket.GetNextSequenceId();
        var senderA = new DispatchContext(101, false);
        var senderB = new DispatchContext(202, false);

        if (ChunkedPacket.TryAcceptChunk(CreateChunk(sequence, 0, 1), senderA, out _, out _))
            return UnitTestResult.Fail("Sender A completed before its final chunk");
        if (ChunkedPacket.TryAcceptChunk(CreateChunk(sequence, 0, 9), senderB, out _, out _))
            return UnitTestResult.Fail("Sender B completed before its final chunk");
        if (!ChunkedPacket.TryAcceptChunk(CreateChunk(sequence, 1, 2), senderA, out byte[] dataA, out DispatchContext contextA))
            return UnitTestResult.Fail("Sender A did not complete");
        if (!ChunkedPacket.TryAcceptChunk(CreateChunk(sequence, 1, 8), senderB, out byte[] dataB, out DispatchContext contextB))
            return UnitTestResult.Fail("Sender B did not complete");

        if (dataA.Length != 2 || dataA[0] != 1 || dataA[1] != 2 || contextA.SenderId != senderA.SenderId)
            return UnitTestResult.Fail("Sender A chunks or context were mixed");
        if (dataB.Length != 2 || dataB[0] != 9 || dataB[1] != 8 || contextB.SenderId != senderB.SenderId)
            return UnitTestResult.Fail("Sender B chunks or context were mixed");

        return UnitTestResult.Pass("Chunk assembly is isolated by sender and preserves context");
    }

	[UnitTest(name: "Protocol compatibility: DLL hash and DLC set are required", category: "Networking")]
	public static UnitTestResult DllHashAndDlcSetAreRequired()
	{
		bool matches = ProtocolCompatibility.Matches(
			ProtocolCompatibility.ModBuildFingerprint,
			ProtocolCompatibility.ActiveDlcIds);
		var mismatchedDlcIds = ProtocolCompatibility.ActiveDlcIds;
		mismatchedDlcIds.Add("TEST_DLC_MISMATCH");
		string mismatchReason = ProtocolCompatibility.BuildMismatchReason(
			ProtocolCompatibility.ModBuildFingerprint, mismatchedDlcIds, true);

		return matches && !ProtocolCompatibility.Matches(
			       ProtocolCompatibility.ModBuildFingerprint, mismatchedDlcIds)
		       && mismatchReason.Contains("Active DLC mismatch", StringComparison.Ordinal)
			? UnitTestResult.Pass("Handshake requires an identical DLL and exact active DLC set")
			: UnitTestResult.Fail("DLL or DLC compatibility gate did not enforce exact equality");
	}

	[UnitTest(name: "Protocol validation: specific error survives transport close", category: "Networking")]
	public static UnitTestResult SpecificValidationErrorSurvivesTransportClose()
	{
		return !GameClient.ShouldTransitionToDisconnected(ClientState.Error)
		       && !GameClient.ShouldTransitionToDisconnected(ClientState.LoadingWorld)
		       && GameClient.ShouldTransitionToDisconnected(ClientState.Connected)
			? UnitTestResult.Pass("Expected disconnects cannot overwrite a specific validation error")
			: UnitTestResult.Fail("Transport close can overwrite a validation or world-load state");
	}

	[UnitTest(name: "Protocol compatibility: DLL hash mismatch is rejected", category: "Networking")]
	public static UnitTestResult DllHashMismatchIsRejected()
	{
		if (ProtocolCompatibility.ModBuildFingerprint.Length != 64)
			return UnitTestResult.Fail("Local mod DLL fingerprint is unavailable");
		if (ProtocolCompatibility.Matches(
		    new string('0', 64), ProtocolCompatibility.ActiveDlcIds))
			return UnitTestResult.Fail("Different mod DLL builds were accepted");

		return UnitTestResult.Pass("Handshake rejects a different mod DLL hash");
	}

	[UnitTest(name: "Protocol compatibility: cannot be bypassed", category: "Networking")]
	public static UnitTestResult ProtocolCompatibilityCannotBeBypassed()
	{
		if (typeof(Configuration).GetProperty("BypassProtocolCompatibilityChecks") != null)
			return UnitTestResult.Fail("Public protocol compatibility bypass is still exposed");
		if (typeof(NetworkSettings).GetProperty("BypassProtocolCompatibilityChecks") != null)
			return UnitTestResult.Fail("Serialized protocol compatibility bypass is still accepted");

		return UnitTestResult.Pass("DLL and DLC validation cannot be disabled");
	}

	[UnitTest(name: "Steam lobby access: proof is identity-bound", category: "Networking")]
	public static UnitTestResult LobbyAccessProofIsIdentityBound()
	{
		string hash = PasswordHelper.HashPassword("correct horse battery staple");
		string challenge = PasswordHelper.CreateChallenge();
		byte[] proof = PasswordHelper.CreateAccessProof(hash, challenge, 100, 200);
		if (!PasswordHelper.VerifyAccessProof(hash, challenge, 100, 200, proof))
			return UnitTestResult.Fail("Exact lobby access proof was rejected");
		if (PasswordHelper.VerifyAccessProof(hash, challenge, 100, 201, proof)
		    || PasswordHelper.VerifyAccessProof(hash, challenge + "x", 100, 200, proof))
			return UnitTestResult.Fail("Lobby proof was not bound to client identity and challenge");

		return UnitTestResult.Pass("Lobby access proof is bound to lobby challenge and client identity");
	}

	[UnitTest(name: "Handshake request: lobby proof has a fixed wire size", category: "Networking")]
	public static UnitTestResult LobbyProofWireSizeIsBounded()
	{
		var packet = new GameStateRequestPacket { LobbyAccessProof = new byte[PasswordHelper.AccessProofBytes] };
		using var stream = new MemoryStream();
		using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
			packet.Serialize(writer);
		stream.Position = 0;
		var decoded = new GameStateRequestPacket();
		using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
			decoded.Deserialize(reader);
		if (decoded.LobbyAccessProof.Length != PasswordHelper.AccessProofBytes)
			return UnitTestResult.Fail("Fixed-size lobby proof did not survive serialization");

		try
		{
			packet.LobbyAccessProof = new byte[PasswordHelper.AccessProofBytes - 1];
			using var rejected = new MemoryStream();
			using var writer = new BinaryWriter(rejected);
			packet.Serialize(writer);
			return UnitTestResult.Fail("Invalid lobby proof length was serialized");
		}
		catch (InvalidDataException)
		{
			return UnitTestResult.Pass("Lobby proof is restricted to empty or fixed-size HMAC");
		}
	}

    [UnitTest(name: "Handshake request: transport sender owns client id", category: "Networking")]
    public static UnitTestResult HandshakeRequestSenderIsBoundToTransport()
    {
        if (!GameStateRequestPacket.IsHostRequestSenderValid(101, 101, false))
            return UnitTestResult.Fail("Matching non-host sender was rejected");
        if (GameStateRequestPacket.IsHostRequestSenderValid(101, 202, false))
            return UnitTestResult.Fail("Spoofed victim client id was accepted");
        if (GameStateRequestPacket.IsHostRequestSenderValid(101, 101, true))
            return UnitTestResult.Fail("Host-origin request was accepted as a client request");

        return UnitTestResult.Pass("Handshake client id is bound to the transport sender");
    }

    [UnitTest(name: "Client relay: known command packets are marked", category: "Networking")]
    public static UnitTestResult KnownClientRelayPacketsAreMarked()
    {
        Type[] packetTypes =
        {
            typeof(PlayerCursorPacket), typeof(ChatMessagePacket), typeof(PingPacket),
            typeof(TrailDeletePacket), typeof(TrailPointsPacket), typeof(ScheduleBlockUpdatePacket),
            typeof(ScheduleAddPacket), typeof(ScheduleDeletePacket), typeof(ScheduleAssignmentPacket),
            typeof(ScheduleRowPacket), typeof(ScheduleDetailsUpdatePacket), typeof(RedAlertStatePacket),
            typeof(SpeedChangePacket), typeof(BuildPacket), typeof(BuildingActionPacket),
            typeof(MinionIdentitySetNamePacket), typeof(ConsumablePermissionPacket),
            typeof(UserNameableChangePacket), typeof(CopySettingsToolPacket), typeof(UtilityBuildPacket),
            typeof(DragToolPacket)
        };

        foreach (Type packetType in packetTypes)
        {
            if (!typeof(IClientRelayable).IsAssignableFrom(packetType))
                return UnitTestResult.Fail($"{packetType.Name} is missing IClientRelayable");
        }

        return UnitTestResult.Pass($"{packetTypes.Length} known client command packets are relayable");
    }

    [UnitTest(name: "Client relay: player identity is transport-bound", category: "Networking")]
    public static UnitTestResult RelayPayloadIdentityMustMatchTransportSender()
    {
        const ulong senderId = 101;
        IPacket[] senderBoundPackets =
        {
            new PlayerCursorPacket { PlayerID = senderId },
            new ChatMessagePacket { SenderId = senderId },
            new PingPacket { PlayerID = senderId },
            new TrailPointsPacket { PlayerID = senderId },
            new TrailDeletePacket { PlayerID = senderId }
        };

        foreach (IPacket packet in senderBoundPackets)
        {
            if (packet is not ISenderBoundRelay)
                return UnitTestResult.Fail($"{packet.GetType().Name} is missing ISenderBoundRelay");
            if (!HostBroadcastPacket.IsInnerSenderValid(packet, senderId))
                return UnitTestResult.Fail($"{packet.GetType().Name} rejected its matching transport sender");
            if (HostBroadcastPacket.IsInnerSenderValid(packet, 202))
                return UnitTestResult.Fail($"{packet.GetType().Name} accepted an impersonated payload sender");
        }

        return UnitTestResult.Pass("Relay payload identities are bound to the transport sender");
    }

    [UnitTest(name: "Client relay: direct transport is rejected by host", category: "Networking")]
	public static UnitTestResult DirectClientRelayIsRejectedButVerifiedNestedRelayIsAccepted()
    {
        var packet = new PingPacket { PlayerID = 101 };
        var directClient = new DispatchContext(101, false);

        if (PacketHandler.CanDispatchPacket(packet, directClient, localIsHost: true))
            return UnitTestResult.Fail("Host accepted a direct client relay packet");
		if (PacketHandler.IsVerifiedClientRelay(directClient.AsVerifiedHostBroadcast(), protocolVerified: false))
			return UnitTestResult.Fail("Host accepted a relay before protocol verification");
		if (!PacketHandler.IsVerifiedClientRelay(directClient.AsVerifiedHostBroadcast(), protocolVerified: true))
            return UnitTestResult.Fail("Host rejected a verified nested relay packet");
        if (!PacketHandler.CanDispatchPacket(packet, new DispatchContext(999, true), localIsHost: false))
            return UnitTestResult.Fail("Client rejected a direct packet from the host");

        return UnitTestResult.Pass("Relay provenance distinguishes direct clients, verified wrappers, and host traffic");
    }

    [UnitTest(name: "Host state: client origins are rejected", category: "Networking")]
    public static UnitTestResult HostOnlyStateRejectsClientOrigins()
    {
        IPacket[] packets =
        {
            new DedicatedServerMessagePacket(), new AllClientsReadyPacket(),
            new ReadyAcceptedPacket(),
            new ChatHistorySyncPacket(), new ToggleMinionKanimEffectPacket(),
            new BuildCompletePacket(), new DeconstructCompletePacket(),
            new ComplexFabricatorSpawnProductPacket(), new GroundItemPickedUpPacket(),
            new PickupItemPacket(), new StorageItemPacket(),
            new WorldDamageSpawnResourcePacket(), new SpawnPrefabPacket(),
			new DuplicantDeathStatePacket(),
            new TelepadEntitySpawnPacket(), new SecureTransferPacket(),
            new TcpTransferStartPacket(), new LargeImpactorStatePacket(),
            new LargeImpactorOutcomePacket(), new ImmigrantOptionsPacket(),
            new AnimSyncPacket(), new MultiToolSyncPacket(),
            new StandardWorker_WorkingState_Packet(), new SymbolOverridePacket(),
            new SymbolVisibilityTogglePacket(), new ChoreErrandsPacket(),
            new ClientReadyStatusUpdatePacket(), new EntityPositionPacket(),
            new EventTriggeredPacket(), new HardSyncCompletePacket(),
            new HardSyncPacket(), new NavigatorPathPacket(), new PlayAnimPacket(),
            new ToggleAnimOverridePacket(), new DuplicantCarryItemPacket(),
            new DuplicantStatePacket(), new ToggleEffectPacket(), new ToolEquipPacket(),
            new VitalStatsPacket(), new DiagnosticPacket(), new NotificationPacket(),
            new DreamBubblePacket(), new ThoughtBubblePacket(), new DigCompletePacket(),
            new BuildingStatePacket(), new OperationalStatePacket(), new ChoreStatePacket(),
            new ConduitContentsPacket(), new DespawnEntityPacket(), new DiggingStatePacket(),
            new DisinfectStatePacket(), new FallingObjectPacket(), new LogicStatePacket(),
            new PlantGrowthStatePacket(), new PlantLifecyclePacket(),
            new ResearchCompletePacket(), new ResearchProgressPacket(), new ResearchStatePacket(),
            new ResourceCountPacket(), new StatusItemsPacket(), new StructureStatePacket(),
            new WorkProgressPacket(), new WorkableProgressPacket(), new WorldCyclePacket(),
            new WorldDataPacket(), new WorldUpdatePacket(), new InstantiationsPacket()
        };

        var directClient = new DispatchContext(101, false);
        var host = new DispatchContext(1, true);
        foreach (IPacket packet in packets)
        {
            if (packet is not IHostOnlyPacket)
                return UnitTestResult.Fail($"{packet.GetType().Name} is missing IHostOnlyPacket");
            if (PacketHandler.CanDispatchPacket(packet, directClient, localIsHost: true))
                return UnitTestResult.Fail($"{packet.GetType().Name} accepted a direct client origin");
            if (PacketHandler.CanDispatchPacket(packet, directClient.AsVerifiedHostBroadcast(), localIsHost: false))
                return UnitTestResult.Fail($"{packet.GetType().Name} accepted a relayed client origin");
            if (!PacketHandler.CanDispatchPacket(packet, host, localIsHost: false))
                return UnitTestResult.Fail($"{packet.GetType().Name} rejected its host origin");
        }

        return UnitTestResult.Pass($"{packets.Length} host-only state packets enforce host origin");
    }

	[UnitTest(name: "Protocol version: authority lifecycle wire is v9", category: "Networking")]
	public static UnitTestResult AuthorityLifecycleWireIsVersionNine()
	{
		return ProtocolCompatibility.CurrentProtocolVersion == 9
			? UnitTestResult.Pass("Protocol v9 pins lifecycle-bound authority state wire")
			: UnitTestResult.Fail(
				$"Expected protocol v9, got {ProtocolCompatibility.CurrentProtocolVersion}");
	}

	[UnitTest(name: "Save transfer: metadata bounds are enforced", category: "Networking")]
	public static UnitTestResult SaveTransferBoundsAreEnforced()
	{
		try
		{
			SaveFileChunkPacket.ValidateMetadata(
				0, SaveFileChunkPacket.MaxChunkBytes,
				SaveFileChunkPacket.MaxChunkBytes, SaveFileChunkPacket.MaxChunkBytes);
		}
		catch (Exception e)
		{
			return UnitTestResult.Fail("Valid save chunk was rejected: " + e.Message);
		}

		if (!SaveMetadataThrows(-1, SaveFileChunkPacket.MaxChunkBytes,
			    SaveFileChunkPacket.MaxChunkBytes, SaveFileChunkPacket.MaxChunkBytes)
		    || !SaveMetadataThrows(0, SaveFileChunkPacket.MaxSaveBytes + 1,
			    SaveFileChunkPacket.MaxChunkBytes, SaveFileChunkPacket.MaxChunkBytes)
		    || !SaveMetadataThrows(0, 1024, SaveFileChunkPacket.MaxChunkBytes + 1, 512)
		    || !SaveMetadataThrows(0, SaveFileChunkPacket.MaxChunkBytes, 1024, 1024)
		    || !SaveMetadataThrows(SaveFileChunkPacket.MaxChunkBytes - 100,
			    SaveFileChunkPacket.MaxChunkBytes, SaveFileChunkPacket.MaxChunkBytes, 200))
		{
			return UnitTestResult.Fail("Invalid save metadata was accepted");
		}

		return UnitTestResult.Pass("Save size, chunk size, offset, and copy bounds are enforced");
	}
	[UnitTest(name: "Save transfer: hash binds identity and payload", category: "Networking")]
	public static UnitTestResult SaveTransferHashBindsMetadata()
	{
		byte[] payload = { 1, 2, 3, 4 };
		byte[] baseline = SecureTransferPacket.ComputePayloadHash(3, "transfer-a", payload);
		if (baseline.Length != 32)
			return UnitTestResult.Fail("SHA-256 output length is not 32 bytes");
		if (ByteArraysEqual(baseline, SecureTransferPacket.ComputePayloadHash(4, "transfer-a", payload)))
			return UnitTestResult.Fail("Sequence number is not bound to the transfer hash");
		if (ByteArraysEqual(baseline, SecureTransferPacket.ComputePayloadHash(3, "transfer-b", payload)))
			return UnitTestResult.Fail("Transfer id is not bound to the transfer hash");
		if (ByteArraysEqual(baseline, SecureTransferPacket.ComputePayloadHash(3, "transfer-a", new byte[] { 1, 2, 3, 5 })))
			return UnitTestResult.Fail("Payload is not bound to the transfer hash");

		return UnitTestResult.Pass("SHA-256 binds sequence, transfer identity, and payload");
	}

	[UnitTest(name: "Mod fingerprint: fields are unambiguous", category: "Networking")]
	public static UnitTestResult ModFingerprintFieldsAreUnambiguous()
	{
		string left = ProtocolCompatibility.ComposeModFingerprint(
			0, "a", "bc", string.Empty, "1", "content", "config");
		string right = ProtocolCompatibility.ComposeModFingerprint(
			0, "ab", "c", string.Empty, "1", "content", "config");
		return string.Equals(left, right, StringComparison.Ordinal)
			? UnitTestResult.Fail("Length-shifted mod metadata collided")
			: UnitTestResult.Pass("Length-prefixed mod metadata is unambiguous");
	}

    [UnitTest(name: "Client relay: HostBroadcast owns one fanout", category: "Networking")]
    public static UnitTestResult HostBroadcastOwnsSingleFanout()
    {
        int dispatchCount = 0;
        int fanoutCount = 0;
        bool sawVerifiedProvenance = false;
        var directClient = new DispatchContext(101, false);

        bool dispatched = HostBroadcastPacket.DispatchVerifiedRelayAndFanOut(
            new PingPacket { PlayerID = 101 },
            directClient,
            (_, context) =>
            {
                dispatchCount++;
                sawVerifiedProvenance = context.IsVerifiedHostBroadcast;
                return true;
            },
            (_, _) => fanoutCount++);

        if (!dispatched || dispatchCount != 1 || fanoutCount != 1)
            return UnitTestResult.Fail($"Expected one dispatch and one fanout, got {dispatchCount} and {fanoutCount}");
        if (!sawVerifiedProvenance)
            return UnitTestResult.Fail("Nested dispatch did not carry verified HostBroadcast provenance");

        fanoutCount = 0;
        bool rejected = HostBroadcastPacket.DispatchVerifiedRelayAndFanOut(
            new PingPacket { PlayerID = 101 }, directClient, (_, _) => false, (_, _) => fanoutCount++);
        if (rejected || fanoutCount != 0)
            return UnitTestResult.Fail("Rejected nested dispatch was still fanned out");

        return UnitTestResult.Pass("HostBroadcast dispatches and fans out an accepted relay exactly once");
    }

    private static bool DeserializeBulkThrows(int packetCount, int firstPacketLength)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(123);
            writer.Write(packetCount);
            if (packetCount == 1)
                writer.Write(firstPacketLength);
        }
        stream.Position = 0;

        try
        {
            using var reader = new BinaryReader(stream);
            new BulkSenderPacket().Deserialize(reader);
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

	private static bool SaveMetadataThrows(int offset, int totalSize, int chunkSize, int length)
	{
		try
		{
			SaveFileChunkPacket.ValidateMetadata(offset, totalSize, chunkSize, length);
			return false;
		}
		catch (InvalidDataException)
		{
			return true;
		}
	}

	private static bool ByteArraysEqual(byte[] left, byte[] right)
	{
		if (left.Length != right.Length)
			return false;
		for (int i = 0; i < left.Length; i++)
		{
			if (left[i] != right[i])
				return false;
		}
		return true;
	}

    private static ChunkedPacket CreateChunk(int sequence, int index, byte value)
    {
        return new ChunkedPacket
        {
            SequenceId = sequence,
            ChunkIndex = index,
            TotalChunks = 2,
            ChunkData = new[] { value }
        };
    }
}
