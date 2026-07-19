#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport;

namespace ONI_Together.DebugTools.UnitTests;

public static class ReliableReconnectIntegrationTests
{
	private const ulong HostId = 1;
	private const ulong ClientId = 2;

	public sealed class ReconnectProbePacket : IPacket
	{
		internal static readonly List<int> Applied = new();
		internal static System.Action Teardown;
		internal int Value;

		public void Serialize(BinaryWriter writer) => writer.Write(Value);
		public void Deserialize(BinaryReader reader) => Value = reader.ReadInt32();

		public void OnDispatched()
		{
			Applied.Add(Value);
			if (Value == 1)
				Teardown?.Invoke();
		}

		internal static void Reset()
		{
			Applied.Clear();
			Teardown = null;
		}
	}

	private sealed class RecordingSender : TransportPacketSender
	{
		internal readonly List<(object Connection, IPacket Packet)> Packets = new();

		public override bool SendPacket(
			object conn,
			IPacket packet,
			PacketSendMode sendType = PacketSendMode.ReliableImmediate)
		{
			Packets.Add((conn, packet));
			return true;
		}
	}

	private sealed class TestEnvironment : IDisposable
	{
		private readonly TransportPacketSender _originalSender = NetworkConfig.TransportPacketSender;
		private readonly bool _originalQueue = Configuration.Instance.EnablePacketQueue;
		private readonly bool _originalHost = MultiplayerSession.IsHost;
		private readonly ulong _originalHostId = MultiplayerSession.HostUserID;
		private readonly Dictionary<ulong, MultiplayerPlayer> _originalPlayers =
			new(MultiplayerSession.ConnectedPlayers);
		private readonly System.Action<DispatchContext> _originalTermination =
			PacketSender.IncomingPageTerminationForTests;
		private readonly bool _originalReadyBypass = PacketHandler.BypassReadyGateForTests;
		private readonly bool _originalTrackingBypass = PacketHandler.BypassTrackingForTests;

		internal readonly RecordingSender Sender = new();
		internal readonly object ReplacementConnection = new();
		internal readonly MultiplayerPlayer Client;
		internal readonly long OldGeneration;
		internal int Terminations;

		internal TestEnvironment()
		{
			Configuration.Instance.EnablePacketQueue = false;
			NetworkConfig.TransportPacketSender = Sender;
			PacketSender.ResetSessionState();
			PacketHandler.ResetSessionState();
			PacketHandler.BypassReadyGateForTests = true;
			PacketHandler.BypassTrackingForTests = true;
			PacketSender.IncomingPageTerminationForTests = _ => Terminations++;
			MultiplayerSession.ConnectedPlayers.Clear();
			MultiplayerSession.IsHost = true;
			MultiplayerSession.HostUserID = HostId;
			Client = new MultiplayerPlayer(ClientId);
			OldGeneration = Client.BeginConnection(new object());
			PrepareClient();
			MultiplayerSession.ConnectedPlayers.Add(ClientId, Client);
		}

		internal void Reconnect() => Client.BeginConnection(ReplacementConnection);

		internal void PrepareClient()
		{
			Client.ProtocolVerified = true;
			Client.readyState = ClientReadyState.Ready;
		}

		public void Dispose()
		{
			ReconnectProbePacket.Reset();
			PacketSender.ResetSessionState();
			PacketHandler.ResetSessionState();
			PacketSender.IncomingPageTerminationForTests = _originalTermination;
			PacketHandler.BypassReadyGateForTests = _originalReadyBypass;
			PacketHandler.BypassTrackingForTests = _originalTrackingBypass;
			MultiplayerSession.ConnectedPlayers.Clear();
			foreach (var pair in _originalPlayers)
				MultiplayerSession.ConnectedPlayers.Add(pair.Key, pair.Value);
			MultiplayerSession.IsHost = _originalHost;
			MultiplayerSession.HostUserID = _originalHostId;
			NetworkConfig.TransportPacketSender = _originalSender;
			Configuration.Instance.EnablePacketQueue = _originalQueue;
		}
	}

	[UnitTest(
		name: "Sync barrier discards stale runtime page without breaking stream",
		category: "Networking")]
	public static UnitTestResult SyncBarrierDiscardsStaleRuntimePage()
	{
		if (!PacketRegistry.HasRegisteredPacket(typeof(ReconnectProbePacket)))
			PacketRegistry.TryRegister(typeof(ReconnectProbePacket));
		using var environment = new TestEnvironment();
		ReconnectProbePacket.Reset();
		var context = new DispatchContext(ClientId, false, environment.OldGeneration);

		environment.Client.readyState = ClientReadyState.Unready;
		if (!Dispatch(1, Page(1, 1), context))
			return UnitTestResult.Fail("Sync barrier rejected the stale runtime page");
		if (ReconnectProbePacket.Applied.Count != 0 || environment.Terminations != 0)
			return UnitTestResult.Fail("Stale runtime frame ran or terminated the page stream");

		environment.Client.readyState = ClientReadyState.Ready;
		if (!Dispatch(2, Page(2, 2), context))
			return UnitTestResult.Fail("Ready runtime page did not continue the stream");
		if (!ReconnectProbePacket.Applied.SequenceEqual(new[] { 2 }))
			return UnitTestResult.Fail("Only the post-barrier runtime frame should apply");
		if (environment.Terminations != 0 || environment.Sender.Packets.Count != 2)
			return UnitTestResult.Fail("The continuous stream returned the wrong ACKs");

		return UnitTestResult.Pass("Stale barrier frame was ACKed and the stream continued");
	}

	[UnitTest(
		name: "Reconnect teardown abandons nested reliable page and ordered remainders",
		category: "Networking")]
	public static UnitTestResult TeardownAbandonsBothNestedRemainders()
	{
		if (!PacketRegistry.HasRegisteredPacket(typeof(ReconnectProbePacket)))
			PacketRegistry.TryRegister(typeof(ReconnectProbePacket));
		using var environment = new TestEnvironment();
		ReconnectProbePacket.Reset();
		ReconnectProbePacket.Teardown = environment.Reconnect;
		var oldContext = new DispatchContext(ClientId, false, environment.OldGeneration);

		if (!Dispatch(2, Page(2, 3), oldContext)
		    || ReconnectProbePacket.Applied.Count != 0)
			return UnitTestResult.Fail("Could not pre-buffer outer sequence two");
		if (!Dispatch(1, Page(1, 1, 2), oldContext))
			return UnitTestResult.Fail("Reconnect teardown rejected the containing envelope");
		string failure = ValidateAbandonedOldGeneration(environment);
		if (failure != null)
			return UnitTestResult.Fail(failure);

		environment.PrepareClient();
		ReconnectProbePacket.Teardown = null;
		var newContext = new DispatchContext(
			ClientId, false, environment.Client.ConnectionGeneration);
		if (!Dispatch(1, Page(1, 4), newContext))
			return UnitTestResult.Fail("Replacement generation could not apply sequence one");
		failure = ValidateReplacementGeneration(environment);
		return failure == null
			? UnitTestResult.Pass("Reconnect abandons both stale layers and restarts at sequence one")
			: UnitTestResult.Fail(failure);
	}

	private static string ValidateAbandonedOldGeneration(TestEnvironment environment)
	{
		if (!ReconnectProbePacket.Applied.SequenceEqual(new[] { 1 }))
			return "A stale inner frame or pre-buffered outer page was applied";
		if (environment.Client.ConnectionGeneration != environment.OldGeneration + 1)
			return "The first inner frame did not establish a replacement generation";
		if (environment.Sender.Packets.Count != 0)
			return "The abandoned old page emitted an ACK";
		return environment.Terminations == 0
			? null
			: "Reconnect teardown was misclassified as a malformed stream";
	}

	private static string ValidateReplacementGeneration(TestEnvironment environment)
	{
		if (!ReconnectProbePacket.Applied.SequenceEqual(new[] { 1, 4 }))
			return "Replacement generation did not apply only its fresh probe";
		if (environment.Terminations != 0 || environment.Sender.Packets.Count != 1)
			return "Replacement generation terminated or returned the wrong ACK count";
		var sent = environment.Sender.Packets[0];
		if (!ReferenceEquals(sent.Connection, environment.ReplacementConnection)
		    || sent.Packet is not OrderedReliablePacket ordered || ordered.Sequence != 1
		    || !TryReadAck(ordered, out ReliablePageAckPacket ack)
		    || ack.TransferId != 1 || ack.PageIndex != 0 || ack.TotalPages != 1)
			return "Replacement generation did not return a fresh sequence-one page ACK";
		return null;
	}

	private static bool Dispatch(
		long sequence, ReliablePagePacket page, DispatchContext context)
		=> PacketHandler.TryHandleIncoming(
			PacketSender.SerializePacketForSending(
				new OrderedReliablePacket(
					sequence, PacketSender.SerializePacketForSending(page))),
			context);

	private static ReliablePagePacket Page(long transferId, params int[] values)
	{
		byte[][] frames = values.Select(value => PacketSender.SerializePacketForSending(
			new ReconnectProbePacket { Value = value })).ToArray();
		using var stream = new MemoryStream();
		using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
		{
			writer.Write(frames.Length);
			foreach (byte[] frame in frames)
			{
				writer.Write(frame.Length);
				writer.Write(frame);
			}
		}
		byte[] batch = stream.ToArray();
		return new ReliablePagePacket(
			transferId, 0, ReliablePageChannel.PageCount(batch.Length), batch.Length, batch);
	}

	private static bool TryReadAck(
		OrderedReliablePacket ordered, out ReliablePageAckPacket ack)
	{
		ack = null;
		try
		{
			using var stream = new MemoryStream(ordered.Payload, writable: false);
			using var reader = new BinaryReader(stream);
			if (reader.ReadInt32() != PacketRegistry.GetPacketId(new ReliablePageAckPacket()))
				return false;
			ack = new ReliablePageAckPacket();
			ack.Deserialize(reader);
			return stream.Position == stream.Length;
		}
		catch
		{
			return false;
		}
	}
}
#endif
