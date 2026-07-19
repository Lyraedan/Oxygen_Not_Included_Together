#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.Handshake;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.States;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class ReliableEnvelopeSecurityTests
	{
		public sealed class ProbeBulkPacket : IPacket, IBulkablePacket
		{
			internal static int Applied;
			internal int Value;
			public int MaxPackSize => 8;
			public uint IntervalMs => 0;
			public void Serialize(BinaryWriter writer) => writer.Write(Value);
			public void Deserialize(BinaryReader reader) => Value = reader.ReadInt32();
			public void OnDispatched()
			{
				Applied++;
				if (Value == 2)
					throw new InvalidDataException("Synthetic second-inner failure");
			}
		}

		[UnitTest(name: "Reliable envelope provenance rejects every recursive bypass once", category: "Networking")]
		public static UnitTestResult RejectsEnvelopeBypassesExactlyOnce()
		{
			int terminations = 0;
			PacketSender.IncomingPageTerminationForTests = _ => terminations++;
			try
			{
				byte[] app = PacketSender.SerializePacketForSending(new AllClientsReadyPacket());
				byte[] batch = Batch(app);
				ReliablePagePacket page = Page(1, batch);
				var ack = new ReliablePageAckPacket(1, 0, 1, batch.Length);
				if (!Reject(PacketSender.SerializePacketForSending(page), 11, ref terminations)
				    || !Reject(PacketSender.SerializePacketForSending(ack), 12, ref terminations))
					return UnitTestResult.Fail("Raw Page or ACK bypass was not terminal");

				var rawApplication = new OrderedReliablePacket(1, app);
				if (!Reject(PacketSender.SerializePacketForSending(rawApplication), 13, ref terminations))
					return UnitTestResult.Fail("Ordered application bypass was not terminal");

				byte[] innerChunk = PacketSender.SerializePacketForSending(new ChunkedPacket
				{
					SequenceId = 22, ChunkIndex = 0, TotalChunks = 1, ChunkData = app
				});
				var outerChunk = new ChunkedPacket
				{
					SequenceId = 21, ChunkIndex = 0, TotalChunks = 1, ChunkData = innerChunk
				};
				if (!Reject(PacketSender.SerializePacketForSending(outerChunk), 14, ref terminations))
					return UnitTestResult.Fail("Recursive Chunked bypass was not terminal");

				byte[] nestedOrdered = PacketSender.SerializePacketForSending(
					new OrderedReliablePacket(1, app));
				var nestedPage = Page(1, Batch(nestedOrdered));
				var outerOrdered = new OrderedReliablePacket(
					1, PacketSender.SerializePacketForSending(nestedPage));
				if (!Reject(PacketSender.SerializePacketForSending(outerOrdered), 15, ref terminations))
					return UnitTestResult.Fail("Page-to-Ordered recursion was not terminal");

				return terminations == 5
					? UnitTestResult.Pass("Every forbidden envelope path terminates its stream exactly once")
					: UnitTestResult.Fail($"Expected 5 terminations, got {terminations}");
			}
			finally
			{
				PacketSender.ResetSessionState();
				PacketHandler.ResetSessionState();
				ChunkedPacket.ResetSessionState();
				PacketSender.IncomingPageTerminationForTests = null;
				PacketHandler.BypassReadyGateForTests = false;
			}
		}

		[UnitTest(name: "Envelope provenance preserves pre-verification handshake gating", category: "Networking")]
		public static UnitTestResult ProvenancePreservesHandshakeGate()
		{
			var direct = new DispatchContext(42, false);
			if (!PacketHandler.CanDispatchPacket(new ChunkedPacket(), direct, localIsHost: true)
			    || PacketHandler.CanDispatchPacket(
				    new ChunkedPacket(), direct.AsChunkReassembled(), localIsHost: true)
			    || !PacketHandler.CanDispatchPacket(
				    new OrderedReliablePacket(), direct.AsChunkReassembled(), localIsHost: true)
			    || PacketHandler.CanDispatchPacket(
				    new ReliablePagePacket(), direct, localIsHost: true)
			    || !PacketHandler.CanDispatchPacket(
				    new ReliablePagePacket(), direct.AsOrderedInner(), localIsHost: true)
			    || !PacketHandler.CanDispatchPacket(
				    new GameStateRequestPacket(), direct.AsReliablePageInner(), localIsHost: true)
			    || PacketHandler.CanDispatchPacket(
				    new WorldUpdatePacket(), direct.AsReliablePageInner(), localIsHost: true))
				return UnitTestResult.Fail("Envelope provenance widened or blocked the inner authority gate");
			return UnitTestResult.Pass("Chunk, Ordered, Page, and application layers keep distinct authority");
		}

		[UnitTest(name: "Deferred wrappers reject recursive envelopes at the exact central bound", category: "Networking")]
		public static UnitTestResult DeferredWrappersRejectRecursionAndCloseBoundary()
		{
			byte[] app = PacketSender.SerializePacketForSending(new AllClientsReadyPacket());
			byte[] ordered = PacketSender.SerializePacketForSending(new OrderedReliablePacket(1, app));
			if (!Throws(() => { _ = new DeferredReliableBatchPacket(new[] { ordered }); })
			    || !Throws(() => { _ = SerializeBody(new DeferredReliablePacket(ordered)); }))
				return UnitTestResult.Fail("Deferred wrapper accepted a transport envelope");

			byte[] recursiveDedicated = BitConverter.GetBytes(
				API_Helper.GetHashCode(typeof(DedicatedServerMessagePacket)));
			var dedicated = new DedicatedServerMessagePacket
			{
				PacketID = BitConverter.ToInt32(recursiveDedicated, 0),
				PacketData = recursiveDedicated
			};
			if (!Throws(() => { _ = SerializeBody(dedicated); }))
				return UnitTestResult.Fail("Dedicated wrapper accepted self-recursion");

			var exactFrame = new byte[DeferredReliableBatchPacket.MaxSerializedBytes - sizeof(int) * 2];
			BitConverter.GetBytes(123).CopyTo(exactFrame, 0);
			byte[] exactBody = SerializeBody(new DeferredReliableBatchPacket(new[] { exactFrame }));
			return exactBody.Length == DeferredReliableBatchPacket.MaxSerializedBytes
			       && exactBody.Length + sizeof(int) * 3 == ReliablePageChannel.MaxQueuedBytes
				? UnitTestResult.Pass("Nested wrappers fail closed and the exact central byte bound is admissible")
				: UnitTestResult.Fail("Deferred batch body and central admission bounds diverged");
		}

		[UnitTest(name: "Page with a rejected second bulk inner withholds its ACK", category: "Networking")]
		public static UnitTestResult BulkSecondInnerFailureWithholdsAck()
		{
			PacketRegistry.TryRegister(typeof(ProbeBulkPacket));
			ProbeBulkPacket.Applied = 0;
			var acks = new List<ReliablePageAckPacket>();
			int terminations = 0;
			var channel = new ReliablePageChannel(
				(_, __) => true, (_, ack) => { acks.Add(ack); return true; },
				PacketHandler.TryHandleIncoming, _ => { }, _ => terminations++);
			int id = API_Helper.GetHashCode(typeof(ProbeBulkPacket));
			var bulk = new BulkSenderPacket(id, new List<byte[]>
			{
				SerializeBody(new ProbeBulkPacket { Value = 1 }),
				SerializeBody(new ProbeBulkPacket { Value = 2 })
			});
			byte[] batch = Batch(PacketSender.SerializePacketForSending(bulk));
			bool originalIsHost = MultiplayerSession.IsHost;
			try
			{
				MultiplayerSession.IsHost = false;
				PacketHandler.ResetSessionState();
				PacketHandler.BypassReadyGateForTests = true;
				bool accepted = channel.AcceptPage(Page(1, batch), new DispatchContext(1, true));
				return !accepted && ProbeBulkPacket.Applied == 2
				       && acks.Count == 0 && terminations == 1
					? UnitTestResult.Pass("Bulk failure propagates through Page without a false final ACK")
					: UnitTestResult.Fail("Bulk swallowed an inner failure or Page emitted a false ACK");
			}
			finally
			{
				MultiplayerSession.IsHost = originalIsHost;
				PacketHandler.BypassReadyGateForTests = false;
			}
		}

		private static bool Reject(byte[] data, ulong senderId, ref int terminations)
		{
			int before = terminations;
			PacketSender.ResetSessionState();
			PacketHandler.ResetSessionState();
			PacketHandler.BypassReadyGateForTests = true;
			ChunkedPacket.ResetSessionState();
			bool accepted = PacketHandler.TryHandleIncoming(
				data, new DispatchContext(senderId, true));
			return !accepted && terminations == before + 1;
		}

		private static byte[] Batch(byte[] frame)
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
			{
				writer.Write(1);
				writer.Write(frame.Length);
				writer.Write(frame);
			}
			return stream.ToArray();
		}

		private static ReliablePagePacket Page(long transferId, byte[] batch)
			=> new ReliablePagePacket(transferId, 0, 1, batch.Length, batch);

		private static byte[] SerializeBody(IPacket packet)
		{
			using var stream = new MemoryStream();
			using var writer = new BinaryWriter(stream);
			packet.Serialize(writer);
			return stream.ToArray();
		}

		private static bool Throws(System.Action action)
		{
			try { action(); return false; }
			catch (InvalidDataException) { return true; }
		}
	}
}
#endif
