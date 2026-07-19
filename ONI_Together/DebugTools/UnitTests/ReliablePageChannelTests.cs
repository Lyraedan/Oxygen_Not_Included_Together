#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.Handshake;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class ReliablePageChannelTests
	{
		private sealed class RecordingSender : TransportPacketSender
		{
			internal readonly List<IPacket> Packets = new();
			public override bool SendPacket(
				object conn, IPacket packet,
				PacketSendMode sendType = PacketSendMode.ReliableImmediate)
			{
				Packets.Add(packet);
				return true;
			}
		}

		[UnitTest(name: "Reliable page channel caps an application burst at two wire pages", category: "Networking")]
		public static UnitTestResult BurstStartsOnlyTwoBoundedPages()
		{
			var sent = new List<ReliablePagePacket>();
			var channel = ReliablePageChannel.CreateForTests(
				(_, page) => { sent.Add(page); return true; });
			object connection = new();
			for (int index = 0; index < 64; index++)
			{
				if (!channel.TryEnqueue(connection, new byte[512]))
					return UnitTestResult.Fail($"Frame {index} was rejected before the documented bound");
			}

			foreach (ReliablePagePacket page in sent)
			{
				byte[] inner = PacketSender.SerializePacketForSending(page);
				var ordered = new OrderedReliablePacket(1, inner);
				if (PacketSender.SerializePacketForSending(ordered).Length
				    > ReliablePageChannel.MaxOrderedWireBytes)
					return UnitTestResult.Fail("A page exceeded six Riptide fragments on its final ordered wire");
			}

			return sent.Count == ReliablePageChannel.MaxInFlightPages
			       && channel.PendingFramesForTests(connection) == 62
				? UnitTestResult.Pass("The first two pages use twelve fragments and the remaining burst stays bounded")
				: UnitTestResult.Fail($"Expected 2 sent / 62 queued, got {sent.Count} / {channel.PendingFramesForTests(connection)}");
		}

		[UnitTest(name: "Reliable page wrapper does not widen the pre-verification authority gate", category: "Networking")]
		public static UnitTestResult PreVerificationChecksTheReassembledInnerPacket()
		{
			if (!PacketHandler.CanDispatchClientPacket(
				    new ReliablePagePacket(1, 0, 1, 8, new byte[8]),
				    protocolVerified: false,
				    ClientReadyState.Unready)
			    || !PacketHandler.CanDispatchClientPacket(
				    new GameStateRequestPacket(),
				    protocolVerified: false,
				    ClientReadyState.Unready)
			    || PacketHandler.CanDispatchClientPacket(
				    new WorldUpdatePacket(),
				    protocolVerified: false,
				    ClientReadyState.Unready))
				return UnitTestResult.Fail("The transport wrapper changed the inner packet authority decision");
			return UnitTestResult.Pass("Unverified pages enter the bounded receiver while only handshake inners dispatch");
		}

		[UnitTest(name: "Reliable page ACKs advance one contiguous credit at a time", category: "Networking")]
		public static UnitTestResult ContiguousAcksAdvanceTheWindow()
		{
			var sent = new List<ReliablePagePacket>();
			var completed = new List<int>();
			var channel = ReliablePageChannel.CreateForTests(
				(_, page) => { sent.Add(page); return true; });
			object connection = new();
			for (int index = 0; index < 4; index++)
				channel.TryEnqueue(connection, BitConverter.GetBytes(index), _ => completed.Add(index));
			if (sent.Count != 2 || channel.PendingFramesForTests(connection) != 2)
				return UnitTestResult.Fail("Initial credit was not exactly two pages");

			if (!channel.AcceptAck(connection, Ack(sent[0])) || sent.Count != 3
			    || channel.PendingFramesForTests(connection) != 0
			    || completed.Count != 1)
				return UnitTestResult.Fail("First contiguous ACK did not coalesce and advance queued frames");
			if (!channel.AcceptAck(connection, Ack(sent[1]))
			    || !channel.AcceptAck(connection, Ack(sent[2]))
			    || completed.Count != 4 || channel.InFlightPagesForTests(connection) != 0)
				return UnitTestResult.Fail("Contiguous final ACKs did not complete every logical frame once");
			if (!channel.AcceptAck(connection, Ack(sent[0])) || completed.Count != 4)
				return UnitTestResult.Fail("A stale ACK released credit or repeated completion");
			return UnitTestResult.Pass("Only exact contiguous ACKs release page credit");
		}

		[UnitTest(name: "Reliable drain waits for queued frames and in-flight pages", category: "Networking")]
		public static UnitTestResult DrainWaitsForQueuedAndInFlightWork()
		{
			TransportPacketSender original = NetworkConfig.TransportPacketSender;
			bool originalQueue = Configuration.Instance.EnablePacketQueue;
			var sender = new RecordingSender();
			object connection = new();
			try
			{
				Configuration.Instance.EnablePacketQueue = false;
				NetworkConfig.TransportPacketSender = sender;
				PacketSender.ResetSessionState();
				if (PacketSender.HasPendingReliable(connection))
					return UnitTestResult.Fail("An unused connection reported pending reliable work");
				for (int index = 0; index < 3; index++)
					if (!PacketSender.SendToConnection(
						    connection, new AllClientsReadyPacket(), PacketSendMode.Reliable))
						return UnitTestResult.Fail($"Reliable frame {index} was rejected");
				if (sender.Packets.Count != ReliablePageChannel.MaxInFlightPages
				    || !PacketSender.HasPendingReliable(connection))
					return UnitTestResult.Fail("A queued frame was reported drained");

				for (int index = 0; index < 3; index++)
				{
					if (index >= sender.Packets.Count
					    || sender.Packets[index] is not OrderedReliablePacket ordered
					    || !TryReadPage(ordered, out ReliablePagePacket page)
					    || !PacketSender.AcceptReliablePageAckForTests(connection, Ack(page)))
						return UnitTestResult.Fail($"Could not acknowledge reliable page {index}");
					if (index < 2 && !PacketSender.HasPendingReliable(connection))
						return UnitTestResult.Fail("An in-flight page was reported drained");
				}
				return !PacketSender.HasPendingReliable(connection)
					? UnitTestResult.Pass("Drain opens only after queued frames and every in-flight page ACK")
					: UnitTestResult.Fail("Fully acknowledged reliable work remained pending");
			}
			finally
			{
				PacketSender.ResetSessionState();
				NetworkConfig.TransportPacketSender = original;
				Configuration.Instance.EnablePacketQueue = originalQueue;
			}
		}

		[UnitTest(name: "Specific reliable completion ignores later pending work", category: "Networking")]
		public static UnitTestResult SpecificCompletionIgnoresLaterPendingWork()
		{
			TransportPacketSender original = NetworkConfig.TransportPacketSender;
			bool originalQueue = Configuration.Instance.EnablePacketQueue;
			var sender = new RecordingSender();
			object connection = new();
			bool targetCompleted = false;
			try
			{
				Configuration.Instance.EnablePacketQueue = false;
				NetworkConfig.TransportPacketSender = sender;
				PacketSender.ResetSessionState();
				if (!PacketSender.SendReliableWithCompletion(
					    connection, new AllClientsReadyPacket(), _ => { })
				    || !PacketSender.SendReliableWithCompletion(
					    connection, new AllClientsReadyPacket(), success => targetCompleted = success)
				    || !PacketSender.SendReliableWithCompletion(
					    connection, new AllClientsReadyPacket(), _ => { }))
					return UnitTestResult.Fail("Could not enqueue prior, target, and later frames");

				if (!Acknowledge(sender, connection, 0)
				    || !Acknowledge(sender, connection, 1)
				    || !targetCompleted
				    || !PacketSender.HasPendingReliable(connection)
				    || SoakStateHashProbe.IsSpecificFenceDeliveryPending(targetCompleted))
					return UnitTestResult.Fail("Target completion remained coupled to later reliable work");
				return UnitTestResult.Pass(
					"The target fence completes while a later reliable frame remains pending");
			}
			finally
			{
				PacketSender.ResetSessionState();
				NetworkConfig.TransportPacketSender = original;
				Configuration.Instance.EnablePacketQueue = originalQueue;
			}
		}

		[UnitTest(name: "Reliable page receiver withholds dispatch across missing and stale epochs", category: "Networking")]
		public static UnitTestResult MissingAndWrongEpochPagesDoNotDispatch()
		{
			var dispatched = new List<byte[]>();
			var terminated = new List<DispatchContext>();
			var acks = new List<ReliablePageAckPacket>();
			var channel = new ReliablePageChannel(
				(_, __) => true,
				(_, ack) => { acks.Add(ack); return true; },
				(payload, _) => { dispatched.Add(payload); return true; },
				_ => { },
				context => terminated.Add(context));
			byte[] batch = Batch(new byte[6000]);
			ReliablePagePacket first = Page(1, batch, 0);
			ReliablePagePacket final = Page(1, batch, 1);
			var epochOne = new DispatchContext(42, false, 7, 11);
			var epochTwo = new DispatchContext(42, false, 7, 12);
			if (!channel.AcceptPage(first, epochOne) || dispatched.Count != 0 || acks.Count != 1)
				return UnitTestResult.Fail("An incomplete transfer dispatched or failed to acknowledge stored progress");
			if (channel.AcceptPage(final, epochTwo) || dispatched.Count != 0 || terminated.Count != 1)
				return UnitTestResult.Fail("A stale epoch completed another epoch's transfer");
			if (!channel.AcceptPage(final, epochOne) || dispatched.Count != 1 || acks.Count != 2)
				return UnitTestResult.Fail("The exact epoch could not finish its contiguous transfer");
			return UnitTestResult.Pass("Missing and stale-epoch pages never expose an inner frame");
		}

		[UnitTest(name: "Reliable page abandons remaining frames after reconnect teardown", category: "Networking")]
		public static UnitTestResult TeardownAbandonsRemainingFrames()
		{
			bool contextCurrent = true;
			int dispatched = 0;
			int acknowledgements = 0;
			int terminations = 0;
			var channel = new ReliablePageChannel(
				(_, __) => true,
				(_, __) => { acknowledgements++; return true; },
				(_, __) =>
				{
					dispatched++;
					contextCurrent = false;
					return true;
				},
				_ => { },
				_ => terminations++,
				isContextCurrent: _ => contextCurrent);
			byte[] batch = Batch(BitConverter.GetBytes(1), BitConverter.GetBytes(2));
			var context = new DispatchContext(1, true, 3, 9);

			if (!channel.AcceptPage(Page(1, batch, 0), context)
			    || dispatched != 1 || acknowledgements != 0 || terminations != 0)
				return UnitTestResult.Fail("Reconnect teardown was treated as malformed page application");
			return UnitTestResult.Pass("Stale page remainder is abandoned without ACK or connection termination");
		}

		[UnitTest(name: "Reliable page queue enforces frame and byte admission bounds", category: "Networking")]
		public static UnitTestResult AdmissionIsBoundedBeforeMutation()
		{
			int terminations = 0;
			var completionResults = new List<bool>();
			var channel = new ReliablePageChannel(
				(_, __) => true, (_, __) => true, (_, __) => true,
				_ => terminations++, _ => { });
			object frameConnection = new();
			for (int index = 0; index < ReliablePageChannel.MaxQueuedFrames; index++)
			{
				if (!channel.TryEnqueue(
					    frameConnection, BitConverter.GetBytes(index), completionResults.Add))
					return UnitTestResult.Fail($"Frame bound rejected entry {index}");
			}
			int acceptedBeforeRejection = channel.QueuedFramesForTests(frameConnection);
			if (channel.TryEnqueue(
				    frameConnection, BitConverter.GetBytes(-1), completionResults.Add)
			    || acceptedBeforeRejection != ReliablePageChannel.MaxQueuedFrames
			    || channel.QueuedFramesForTests(frameConnection) != 0
			    || completionResults.Count != ReliablePageChannel.MaxQueuedFrames + 1
			    || completionResults.Any(result => result)
			    || terminations != 1
			    || channel.TryEnqueue(
				    frameConnection, BitConverter.GetBytes(-2), completionResults.Add)
			    || terminations != 1 || completionResults[^1])
				return UnitTestResult.Fail("Frame overflow mutated admission or failed to terminate exactly once");

			object byteConnection = new();
			int byteTerminations = terminations;
			if (!channel.TryEnqueue(
				    byteConnection, new byte[ReliablePageChannel.MaxQueuedBytes - sizeof(int) * 2])
			    || channel.TryEnqueue(byteConnection, BitConverter.GetBytes(1))
			    || terminations != byteTerminations + 1)
				return UnitTestResult.Fail("Byte admission mutated or exceeded the 16 MiB bound");
			return UnitTestResult.Pass("Overflow rejects before admission, fails callbacks, and terminates once");
		}

		[UnitTest(name: "Reliable PacketSender terminates after application admission overflow", category: "Networking")]
		public static UnitTestResult PacketSenderAdmissionOverflowTerminates()
		{
			TransportPacketSender original = NetworkConfig.TransportPacketSender;
			bool originalQueue = Configuration.Instance.EnablePacketQueue;
			var sender = new RecordingSender();
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
				for (int index = 0; index < ReliablePageChannel.MaxQueuedFrames; index++)
				{
					if (!PacketSender.SendToConnection(
						    connection, new AllClientsReadyPacket(), PacketSendMode.Reliable))
						return UnitTestResult.Fail($"PacketSender rejected entry {index} before its bound");
				}
				if (PacketSender.SendToConnection(
					    connection, new AllClientsReadyPacket(), PacketSendMode.Reliable)
				    || terminations != 1
				    || PacketSender.SendToConnection(
					    connection, new AllClientsReadyPacket(), PacketSendMode.Reliable)
				    || terminations != 1)
					return UnitTestResult.Fail("PacketSender silently dropped overflow or repeated termination");
				return UnitTestResult.Pass("PacketSender overflow is observable even when callers ignore its result");
			}
			finally
			{
				PacketSender.ResetSessionState();
				PacketSender.OutgoingPageTerminationForTests = null;
				NetworkConfig.TransportPacketSender = original;
				Configuration.Instance.EnablePacketQueue = originalQueue;
			}
		}

		[UnitTest(name: "Reliable page drop and reset fail retained completions", category: "Networking")]
		public static UnitTestResult DropAndResetReleaseAllState()
		{
			var results = new List<bool>();
			var channel = ReliablePageChannel.CreateForTests((_, __) => true);
			object dropped = new();
			channel.TryEnqueue(dropped, BitConverter.GetBytes(1), results.Add);
			channel.TryEnqueue(dropped, BitConverter.GetBytes(2), results.Add);
			channel.DropConnection(dropped);
			object reset = new();
			channel.TryEnqueue(reset, BitConverter.GetBytes(3), results.Add);
			channel.Reset();
			return results.SequenceEqual(new[] { false, false, false })
			       && channel.InFlightPagesForTests(dropped) == 0
			       && channel.InFlightPagesForTests(reset) == 0
				? UnitTestResult.Pass("Drop and reset fail callbacks and release queued bytes")
				: UnitTestResult.Fail("Drop or reset retained state or reported false success");
		}

		[UnitTest(name: "Reliable page ACK lease terminates a stalled connection", category: "Networking")]
		public static UnitTestResult AckLeaseTerminatesStalledTransfer()
		{
			System.DateTime now = new System.DateTime(
				2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
			int terminations = 0;
			var completions = new List<bool>();
			var channel = new ReliablePageChannel(
				(_, __) => true, (_, __) => true, (_, __) => true,
				_ => terminations++, _ => { }, () => now);
			object connection = new();
			if (!channel.TryEnqueue(connection, BitConverter.GetBytes(1), completions.Add))
				return UnitTestResult.Fail("Could not start ACK lease");
			now += ReliablePageChannel.AckTimeout + TimeSpan.FromMilliseconds(1);
			channel.ExpireStalledTransfers();
			channel.ExpireStalledTransfers();
			return terminations == 1 && completions.SequenceEqual(new[] { false })
			       && !channel.TryEnqueue(connection, BitConverter.GetBytes(2), completions.Add)
			       && completions.SequenceEqual(new[] { false, false })
				? UnitTestResult.Pass("Missing application ACK fails callbacks and terminates once")
				: UnitTestResult.Fail("Stalled application ACK remained silent or terminated twice");
		}

		[UnitTest(name: "Reliable page ACK control bypasses an active outbound transfer", category: "Networking")]
		public static UnitTestResult AckControlBypassesTheApplicationWindow()
		{
			TransportPacketSender original = NetworkConfig.TransportPacketSender;
			bool originalQueue = Configuration.Instance.EnablePacketQueue;
			var sender = new RecordingSender();
			object connection = new();
			try
			{
				Configuration.Instance.EnablePacketQueue = false;
				NetworkConfig.TransportPacketSender = sender;
				PacketSender.ResetSessionState();
				if (!PacketSender.SendToConnection(
					    connection, new AllClientsReadyPacket(), PacketSendMode.ReliableImmediate)
				    || !PacketSender.SendToConnection(
					    connection, new ReliablePageAckPacket(9, 0, 1, 12),
					    PacketSendMode.ReliableImmediate)
				    || sender.Packets.Count != 2
				    || sender.Packets.Any(packet => packet is not OrderedReliablePacket))
					return UnitTestResult.Fail("ACK control waited behind its own direction's application credit");
				var first = (OrderedReliablePacket)sender.Packets[0];
				var second = (OrderedReliablePacket)sender.Packets[1];
				if (BitConverter.ToInt32(first.Payload, 0) != PacketRegistry.GetPacketId(new ReliablePagePacket())
				    || BitConverter.ToInt32(second.Payload, 0) != PacketRegistry.GetPacketId(new ReliablePageAckPacket()))
					return UnitTestResult.Fail("Application or ACK used the wrong transport wrapper path");
				return UnitTestResult.Pass("ACK control remains ordered-reliable without consuming page credit");
			}
			finally
			{
				PacketSender.ResetSessionState();
				NetworkConfig.TransportPacketSender = original;
				Configuration.Instance.EnablePacketQueue = originalQueue;
			}
		}

		[UnitTest(name: "Oversized unreliable application send is rejected before transport chunking", category: "Networking")]
		public static UnitTestResult OversizedUnreliableIsRejected()
		{
			TransportPacketSender original = NetworkConfig.TransportPacketSender;
			bool originalQueue = Configuration.Instance.EnablePacketQueue;
			var sender = new RecordingSender();
			try
			{
				Configuration.Instance.EnablePacketQueue = false;
				NetworkConfig.TransportPacketSender = sender;
				var packet = new DeferredReliableBatchPacket(new[] { new byte[6000] });
				return !PacketSender.SendToConnection(new object(), packet, PacketSendMode.Unreliable)
				       && sender.Packets.Count == 0
					? UnitTestResult.Pass("Unreliable payload cannot synchronously consume over six fragments")
					: UnitTestResult.Fail("Oversized unreliable payload reached the transport or was upgraded");
			}
			finally
			{
				PacketSender.ResetSessionState();
				NetworkConfig.TransportPacketSender = original;
				Configuration.Instance.EnablePacketQueue = originalQueue;
			}
		}

		private static ReliablePageAckPacket Ack(ReliablePagePacket page)
			=> new ReliablePageAckPacket(
				page.TransferId, page.PageIndex, page.TotalPages, page.TotalBytes);

		private static bool TryReadPage(
			OrderedReliablePacket envelope, out ReliablePagePacket page)
		{
			page = null;
			try
			{
				using var stream = new MemoryStream(envelope.Payload, writable: false);
				using var reader = new BinaryReader(stream);
				if (reader.ReadInt32() != PacketRegistry.GetPacketId(new ReliablePagePacket()))
					return false;
				page = new ReliablePagePacket();
				page.Deserialize(reader);
				return stream.Position == stream.Length;
			}
			catch
			{
				return false;
			}
		}

		private static bool Acknowledge(
			RecordingSender sender, object connection, int index)
		{
			return index < sender.Packets.Count
			       && sender.Packets[index] is OrderedReliablePacket ordered
			       && TryReadPage(ordered, out ReliablePagePacket page)
			       && PacketSender.AcceptReliablePageAckForTests(connection, Ack(page));
		}

		private static byte[] Batch(params byte[][] frames)
		{
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
			return stream.ToArray();
		}

		private static ReliablePagePacket Page(long transferId, byte[] batch, int pageIndex)
		{
			int length = ReliablePageChannel.PageLength(batch.Length, pageIndex);
			var data = new byte[length];
			Buffer.BlockCopy(batch, pageIndex * ReliablePageChannel.MaxPageDataBytes, data, 0, length);
			return new ReliablePagePacket(
				transferId, pageIndex, ReliablePageChannel.PageCount(batch.Length), batch.Length, data);
		}
	}
}
#endif
