#if DEBUG
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.Transport;
using ONI_Together.Networking.Transport.Lan;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class OrderedReliableChannelTests
	{
		private sealed class RecordingSender : TransportPacketSender
		{
			internal readonly List<IPacket> Packets = new();
			internal bool RefuseNext;

			public override bool SendPacket(
				object conn, IPacket packet,
				PacketSendMode sendType = PacketSendMode.ReliableImmediate)
			{
				lock (Packets)
				{
					Packets.Add(packet);
					if (!RefuseNext)
						return true;
					RefuseNext = false;
					return false;
				}
			}
		}

		[UnitTest(name: "Ordered reliable channel drains out-of-order packets causally", category: "Networking")]
		public static UnitTestResult DrainsOutOfOrderPacketsCausally()
		{
			var buffer = new OrderedReliableReceiveBuffer(maxPending: 8, maxBytes: 64);
			var applied = new List<int>();
			byte[] first = System.BitConverter.GetBytes(1);
			byte[] second = System.BitConverter.GetBytes(2);
			if (buffer.Accept(2, second, payload => applied.Add(System.BitConverter.ToInt32(payload, 0)))
					!= OrderedReliableAcceptResult.Buffered
			    || applied.Count != 0
			    || buffer.Accept(1, first, payload => applied.Add(System.BitConverter.ToInt32(payload, 0)))
					!= OrderedReliableAcceptResult.Applied
			    || !applied.SequenceEqual(new[] { 1, 2 })
			    || buffer.Accept(2, second, payload => applied.Add(System.BitConverter.ToInt32(payload, 0)))
					!= OrderedReliableAcceptResult.Duplicate)
				return UnitTestResult.Fail("Reliable packets were applied before their causal predecessor");
			return UnitTestResult.Pass("Out-of-order reliable packets drain exactly once in sequence");
		}

		[UnitTest(name: "Ordered reliable channel bounds unresolved gaps", category: "Networking")]
		public static UnitTestResult BoundsUnresolvedGaps()
		{
			var buffer = new OrderedReliableReceiveBuffer(maxPending: 1, maxBytes: 4);
			byte[] first = System.BitConverter.GetBytes(1);
			byte[] second = System.BitConverter.GetBytes(2);
			byte[] third = System.BitConverter.GetBytes(3);
			if (buffer.Accept(2, second, _ => { }) != OrderedReliableAcceptResult.Buffered
			    || buffer.Accept(3, third, _ => { }) != OrderedReliableAcceptResult.Overflow
			    || buffer.Accept(1, first, _ => { }) != OrderedReliableAcceptResult.Terminated)
				return UnitTestResult.Fail("An unresolved reliable gap exceeded its bounded buffer");
			return UnitTestResult.Pass("Gap overflow terminates the stream without partial replay");
		}

		[UnitTest(name: "Ordered reliable send sequence commits only accepted sends", category: "Networking")]
		public static UnitTestResult SendSequenceCommitsOnlyAcceptedSends()
		{
			var cursor = new OrderedReliableSendCursor();
			if (cursor.Reserve() != 1 || cursor.Reserve() != 1
			    || cursor.Commit(2) || !cursor.Commit(1) || cursor.Reserve() != 2)
				return UnitTestResult.Fail("A failed send created an unrecoverable reliable sequence gap");
			return UnitTestResult.Pass("Rejected sends reuse their sequence until transport acceptance");
		}

		[UnitTest(name: "Ordered reliable envelope preserves sequence and payload", category: "Networking")]
		public static UnitTestResult EnvelopeRoundTripPreservesPayload()
		{
			var packet = new OrderedReliablePacket(7, new byte[] { 1, 2, 3, 4 });
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				packet.Serialize(writer);
			stream.Position = 0;
			var copy = new OrderedReliablePacket();
			using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
				copy.Deserialize(reader);
			return copy.Sequence == 7 && copy.Payload.SequenceEqual(new byte[] { 1, 2, 3, 4 })
				? UnitTestResult.Pass("Ordered envelope round-trips its causal marker and payload")
				: UnitTestResult.Fail("Ordered envelope lost its causal marker or payload");
		}

		[UnitTest(name: "Ordered reliable envelope is registry-constructible", category: "Networking")]
		public static UnitTestResult EnvelopeIsRegistryConstructible()
		{
			var packet = new OrderedReliablePacket(1, System.BitConverter.GetBytes(1));
			int packetId = PacketRegistry.GetPacketId(packet);
			return PacketRegistry.Create(packetId) is OrderedReliablePacket
				? UnitTestResult.Pass("Packet registry can construct the ordered envelope on receive")
				: UnitTestResult.Fail("Packet registry could not construct the ordered envelope");
		}

		[UnitTest(name: "Ordered reliable channel flushes bulk before later direct packets", category: "Networking")]
		public static UnitTestResult BulkCannotBeOvertakenByDirectReliable()
		{
			TransportPacketSender originalSender = NetworkConfig.TransportPacketSender;
			bool originalQueueSetting = Configuration.Instance.EnablePacketQueue;
			var sender = new RecordingSender();
			object connection = new();
			try
			{
				Configuration.Instance.EnablePacketQueue = false;
				NetworkConfig.TransportPacketSender = sender;
				PacketSender.ResetSessionState();
				if (!PacketSender.SendToConnection(
					    connection, new StorageItemPacket(), PacketSendMode.Reliable)
				    || !PacketSender.SendToConnection(
					    connection, new AllClientsReadyPacket(), PacketSendMode.Reliable))
					return UnitTestResult.Fail("A synthetic accepted reliable send failed");
				if (sender.Packets.Count != 2
				    || sender.Packets[0] is not OrderedReliablePacket bulk
				    || sender.Packets[1] is not OrderedReliablePacket direct
				    || bulk.Sequence != 1 || direct.Sequence != 2
				    || !ReadPageFramePacketIds(bulk).SequenceEqual(new[]
					{ PacketRegistry.GetPacketId(new BulkSenderPacket()) })
				    || !ReadPageFramePacketIds(direct).SequenceEqual(new[]
					{ PacketRegistry.GetPacketId(new AllClientsReadyPacket()) }))
					return UnitTestResult.Fail("A later direct reliable packet overtook queued bulk state");
				return UnitTestResult.Pass("Pending bulk state receives the earlier causal sequence");
			}
			finally
			{
				PacketSender.ResetSessionState();
				NetworkConfig.TransportPacketSender = originalSender;
				Configuration.Instance.EnablePacketQueue = originalQueueSetting;
			}
		}

		[UnitTest(name: "Ordered reliable channel blocks direct send when bulk preflush fails", category: "Networking")]
		public static UnitTestResult FailedBulkPreflushBlocksLaterDirectReliable()
		{
			TransportPacketSender originalSender = NetworkConfig.TransportPacketSender;
			bool originalQueueSetting = Configuration.Instance.EnablePacketQueue;
			var sender = new RecordingSender { RefuseNext = true };
			object connection = new();
			try
			{
				Configuration.Instance.EnablePacketQueue = false;
				NetworkConfig.TransportPacketSender = sender;
				PacketSender.ResetSessionState();
				PacketSender.SendToConnection(
					connection, new StorageItemPacket(), PacketSendMode.Reliable);
				if (PacketSender.SendToConnection(
					    connection, new AllClientsReadyPacket(), PacketSendMode.Reliable)
				    || sender.Packets.Count != 1
				    || OrderedReliableChannel.CurrentOutgoingSequence(connection) != 0)
					return UnitTestResult.Fail("Direct reliable state passed a refused bulk predecessor");
				if (PacketSender.SendToConnection(
					    connection, new AllClientsReadyPacket(), PacketSendMode.Reliable)
				    || sender.Packets.Count != 1
				    || OrderedReliableChannel.CurrentOutgoingSequence(connection) != 0
				    || PacketSender.PendingBulkCountForTests(connection) != 1)
					return UnitTestResult.Fail("A terminal page send fabricated retry success or discarded bulk state");
				return UnitTestResult.Pass("A refused page preserves bulk state and terminates before cursor commit");
			}
			finally
			{
				PacketSender.ResetSessionState();
				NetworkConfig.TransportPacketSender = originalSender;
				Configuration.Instance.EnablePacketQueue = originalQueueSetting;
			}
		}

		[UnitTest(name: "Ordered reliable bulk preserves cross-type call order", category: "Networking")]
		public static UnitTestResult CrossTypeBulkPreservesCallOrder()
		{
			TransportPacketSender originalSender = NetworkConfig.TransportPacketSender;
			bool originalQueueSetting = Configuration.Instance.EnablePacketQueue;
			var sender = new RecordingSender();
			object connection = new();
			try
			{
				Configuration.Instance.EnablePacketQueue = false;
				NetworkConfig.TransportPacketSender = sender;
				PacketSender.ResetSessionState();
				if (!PacketSender.SendToConnection(
					    connection, new StorageItemPacket(), PacketSendMode.Reliable)
				    || !PacketSender.SendToConnection(
					    connection, new PrioritizeStatePacket(), PacketSendMode.Reliable)
				    || !PacketSender.SendToConnection(
					    connection, new AllClientsReadyPacket(), PacketSendMode.Reliable))
					return UnitTestResult.Fail("Accepted cross-type bulk sequence failed");
				if (sender.Packets.Count != 2
				    || sender.Packets[0] is not OrderedReliablePacket first || first.Sequence != 1
				    || sender.Packets[1] is not OrderedReliablePacket second || second.Sequence != 2
				    || ReadBulkInnerPacketId(first) != PacketRegistry.GetPacketId(new StorageItemPacket())
				    || ReadBulkInnerPacketId(second) != PacketRegistry.GetPacketId(new PrioritizeStatePacket())
				    || !TryReadPage(first, out ReliablePagePacket firstPage)
				    || !PacketSender.AcceptReliablePageAckForTests(
					    connection, new ReliablePageAckPacket(
						    firstPage.TransferId, firstPage.PageIndex,
						    firstPage.TotalPages, firstPage.TotalBytes))
				    || sender.Packets.Count != 3
				    || sender.Packets[2] is not OrderedReliablePacket direct || direct.Sequence != 3
				    || !ReadPageFramePacketIds(direct).SequenceEqual(new[]
					{ PacketRegistry.GetPacketId(new AllClientsReadyPacket()) }))
					return UnitTestResult.Fail("Cross-type bulk calls were reordered by packet-id batching");
				return UnitTestResult.Pass("Switching bulk types closes the earlier causal batch first");
			}
			finally
			{
				PacketSender.ResetSessionState();
				NetworkConfig.TransportPacketSender = originalSender;
				Configuration.Instance.EnablePacketQueue = originalQueueSetting;
			}
		}

		[UnitTest(name: "Riptide chunk send propagates partial refusal", category: "Networking")]
		public static UnitTestResult RiptideChunkSendPropagatesPartialRefusal()
		{
			int attempts = 0;
			bool accepted = RiptidePacketSender.SendAllChunks(3, index =>
			{
				attempts++;
				return index != 1;
			});
			return !accepted && attempts == 2
				? UnitTestResult.Pass("Chunk refusal stops the send before ordered cursor commit")
				: UnitTestResult.Fail("A partial Riptide chunk send was reported as accepted");
		}

		[UnitTest(name: "Ordered reliable channel rejects recursive envelopes", category: "Networking")]
		public static UnitTestResult RecursiveEnvelopeIsRejected()
		{
			byte[] payload = System.BitConverter.GetBytes(
				PacketRegistry.GetPacketId(new OrderedReliablePacket()));
			return OrderedReliableChannel.IsNestedEnvelopePayload(payload)
				? UnitTestResult.Pass("Recursive ordered envelopes are rejected before dispatch")
				: UnitTestResult.Fail("Recursive ordered envelope could reach unbounded dispatch recursion");
		}

		[UnitTest(name: "Ordered reliable channel terminates after inner rejection", category: "Networking")]
		public static UnitTestResult InnerRejectionTerminatesStream()
		{
			var buffer = new OrderedReliableReceiveBuffer(maxPending: 8, maxBytes: 64);
			byte[] payload = System.BitConverter.GetBytes(1);
			if (buffer.Accept(1, payload, _ => throw new InvalidDataException("synthetic"))
					!= OrderedReliableAcceptResult.Overflow
			    || buffer.Accept(1, payload, _ => { }) != OrderedReliableAcceptResult.Terminated)
				return UnitTestResult.Fail("Rejected inner payload left the causal stream active");
			return UnitTestResult.Pass("Malformed inner payload terminates without sequence advancement");
		}

		[UnitTest(name: "Ordered reliable incoming streams isolate generation and epoch", category: "Networking")]
		public static UnitTestResult IncomingStreamsIsolateGenerationAndEpoch()
		{
			var original = new DispatchContext(42, false, 7, 11);
			var nextGeneration = new DispatchContext(42, false, 8, 11);
			var nextEpoch = new DispatchContext(42, false, 7, 12);
			OrderedReliableChannel.ResetSessionState();
			try
			{
				OrderedReliableReceiveBuffer originalBuffer =
					OrderedReliableChannel.GetOrCreateIncoming(original);
				OrderedReliableReceiveBuffer generationBuffer =
					OrderedReliableChannel.GetOrCreateIncoming(nextGeneration);
				OrderedReliableReceiveBuffer epochBuffer =
					OrderedReliableChannel.GetOrCreateIncoming(nextEpoch);
				var originalApplied = new List<int>();
				var generationApplied = new List<int>();
				var epochApplied = new List<int>();

				if (!ReferenceEquals(
					    originalBuffer, OrderedReliableChannel.GetOrCreateIncoming(original))
				    || ReferenceEquals(originalBuffer, generationBuffer)
				    || ReferenceEquals(originalBuffer, epochBuffer)
				    || ReferenceEquals(generationBuffer, epochBuffer)
				    || originalBuffer.Accept(2, System.BitConverter.GetBytes(2),
					    payload => originalApplied.Add(System.BitConverter.ToInt32(payload, 0)))
					    != OrderedReliableAcceptResult.Buffered
				    || generationBuffer.Accept(1, System.BitConverter.GetBytes(10),
					    payload => generationApplied.Add(System.BitConverter.ToInt32(payload, 0)))
					    != OrderedReliableAcceptResult.Applied
				    || epochBuffer.Accept(1, System.BitConverter.GetBytes(20),
					    payload => epochApplied.Add(System.BitConverter.ToInt32(payload, 0)))
					    != OrderedReliableAcceptResult.Applied
				    || originalBuffer.Accept(1, System.BitConverter.GetBytes(1),
					    payload => originalApplied.Add(System.BitConverter.ToInt32(payload, 0)))
					    != OrderedReliableAcceptResult.Applied
				    || !originalApplied.SequenceEqual(new[] { 1, 2 })
				    || !generationApplied.SequenceEqual(new[] { 10 })
				    || !epochApplied.SequenceEqual(new[] { 20 }))
					return UnitTestResult.Fail("A replacement connection consumed another stream's cursor or gap");
				return UnitTestResult.Pass("Sender, generation, and session epoch own independent receive streams");
			}
			finally
			{
				OrderedReliableChannel.ResetSessionState();
			}
		}

		[UnitTest(name: "Ordered reliable diagnostics count only inner packets", category: "Networking")]
		public static UnitTestResult DiagnosticsCountOnlyInnerPackets()
		{
			if (PacketHandler.ShouldTrackIncoming(new OrderedReliablePacket())
			    || PacketHandler.ShouldTrackIncoming(new ReliablePagePacket())
			    || PacketHandler.ShouldTrackIncoming(new ReliablePageAckPacket())
			    || PacketHandler.ShouldTrackIncoming(new ChunkedPacket())
			    || !PacketHandler.ShouldTrackIncoming(new AllClientsReadyPacket()))
				return UnitTestResult.Fail("Packet diagnostics counted both envelope and logical payload");
			return UnitTestResult.Pass("Packet diagnostics omit the transport envelope");
		}

		[UnitTest(name: "Ordered reliable envelope can carry the initial handshake", category: "Networking")]
		public static UnitTestResult EnvelopeCanCarryInitialHandshake()
		{
			if (!PacketHandler.CanDispatchClientPacket(
				    new OrderedReliablePacket(), protocolVerified: false, ClientReadyState.Unready))
				return UnitTestResult.Fail("Host rejected the envelope before inspecting initial handshake");
			if (PacketHandler.CanDispatchClientPacket(
				    new AllClientsReadyPacket(), protocolVerified: false, ClientReadyState.Unready))
				return UnitTestResult.Fail("Envelope bypass would authorize a non-handshake inner packet");
			return UnitTestResult.Pass("Outer envelope passes pre-auth; inner packet keeps the real authority gate");
		}

		[UnitTest(name: "Ordered reliable concurrent sends use unique contiguous sequences", category: "Networking")]
		public static UnitTestResult ConcurrentSendsUseUniqueContiguousSequences()
		{
			TransportPacketSender originalSender = NetworkConfig.TransportPacketSender;
			bool originalQueueSetting = Configuration.Instance.EnablePacketQueue;
			var sender = new RecordingSender();
			object connection = new();
			const int count = 8;
			var results = new bool[count];
			var threads = new Thread[count];
			bool timedOut = false;
			try
			{
				Configuration.Instance.EnablePacketQueue = false;
				NetworkConfig.TransportPacketSender = sender;
				PacketSender.ResetSessionState();
				for (int i = 0; i < count; i++)
				{
					int index = i;
					threads[i] = new Thread(() => results[index] = PacketSender.SendToConnection(
						connection, new AllClientsReadyPacket(), PacketSendMode.Reliable))
					{
						IsBackground = true
					};
					threads[i].Start();
				}
				foreach (Thread thread in threads)
				{
					if (!thread.Join(millisecondsTimeout: 2_000))
					{
						timedOut = true;
						return UnitTestResult.Fail("Concurrent reliable send serialization deadlocked");
					}
				}
				long[] sequences;
				lock (sender.Packets)
					sequences = sender.Packets.Cast<OrderedReliablePacket>()
						.Select(packet => packet.Sequence).OrderBy(sequence => sequence).ToArray();
				if (results.Any(result => !result) || sequences.Length != 2
				    || !sequences.SequenceEqual(new long[] { 1, 2 })
				    || !TryReadPage((OrderedReliablePacket)sender.Packets[0], out ReliablePagePacket first)
				    || !PacketSender.AcceptReliablePageAckForTests(
					    connection, new ReliablePageAckPacket(
						    first.TransferId, first.PageIndex, first.TotalPages, first.TotalBytes)))
					return UnitTestResult.Fail("Concurrent reliable sends reused or skipped an application sequence");
				lock (sender.Packets)
				{
					if (sender.Packets.Count != 3
					    || sender.Packets.Cast<OrderedReliablePacket>()
						    .Select(packet => packet.Sequence).Distinct().Count() != 3
					    || sender.Packets.Cast<OrderedReliablePacket>()
						    .Sum(packet => ReadPageFramePacketIds(packet).Count) != count)
						return UnitTestResult.Fail("Concurrent frames were lost while coalescing after ACK");
				}
				return UnitTestResult.Pass("Connection serialization keeps contiguous pages and all concurrent frames");
			}
			finally
			{
				if (!timedOut)
					PacketSender.ResetSessionState();
				NetworkConfig.TransportPacketSender = originalSender;
				Configuration.Instance.EnablePacketQueue = originalQueueSetting;
			}
		}

		[UnitTest(name: "Ordered reliable reconnect starts a new connection stream", category: "Networking")]
		public static UnitTestResult ReconnectStartsNewConnectionStream()
		{
			var oldConnection = new object();
			var newConnection = new object();
			PacketSender.ResetSessionState();
			try
			{
				var oldPacket = OrderedReliableChannel.PrepareOutgoing(
					oldConnection, new AllClientsReadyPacket());
				if (oldPacket.Sequence != 1
				    || !OrderedReliableChannel.CommitOutgoing(oldConnection, oldPacket.Sequence))
					return UnitTestResult.Fail("Old connection did not establish sequence one");
				PacketSender.DropConnection(oldConnection);
				var replacement = OrderedReliableChannel.PrepareOutgoing(
					newConnection, new AllClientsReadyPacket());
				return replacement.Sequence == 1
					? UnitTestResult.Pass("Replacement connection owns a fresh ordered stream")
					: UnitTestResult.Fail("Replacement connection inherited the stale ordered cursor");
			}
			finally
			{
				PacketSender.ResetSessionState();
			}
		}

		private static int ReadBulkInnerPacketId(OrderedReliablePacket envelope)
		{
			List<int> pageFrames = ReadPageFramePacketIds(envelope);
			if (pageFrames.Count != 1
			    || pageFrames[0] != PacketRegistry.GetPacketId(new BulkSenderPacket())
			    || !TryReadPage(envelope, out ReliablePagePacket page))
				return 0;
			using var stream = new MemoryStream(page.Data, writable: false);
			using var reader = new BinaryReader(stream);
			reader.ReadInt32();
			int frameLength = reader.ReadInt32();
			reader.ReadInt32();
			return frameLength >= sizeof(int) * 2 ? reader.ReadInt32() : 0;
		}

		private static List<int> ReadPageFramePacketIds(OrderedReliablePacket envelope)
		{
			var result = new List<int>();
			if (!TryReadPage(envelope, out ReliablePagePacket page) || page.TotalPages != 1)
				return result;
			using var stream = new MemoryStream(page.Data, writable: false);
			using var reader = new BinaryReader(stream);
			int count = reader.ReadInt32();
			for (int index = 0; index < count; index++)
			{
				int length = reader.ReadInt32();
				if (length < sizeof(int) || length > stream.Length - stream.Position)
					return new List<int>();
				result.Add(reader.ReadInt32());
				stream.Position += length - sizeof(int);
			}
			return stream.Position == stream.Length ? result : new List<int>();
		}

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
	}
}
#endif
