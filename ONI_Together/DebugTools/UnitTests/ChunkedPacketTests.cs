using System;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;

namespace ONI_Together.DebugTools.UnitTests
{
	/// <summary>
	/// The chunking path is the one piece of the LiteNetLib payload-budget work that has never
	/// executed: the conduit batch was trimmed to fit inside the budget, so nothing has yet
	/// crossed it in a live session, and SendChunked logs nothing - "no chunk lines in the log"
	/// is equally consistent with "never ran" and "ran every tick".
	///
	/// These drive the real reassembly (ChunkedPacket.TryReassemble, the same call OnDispatched
	/// makes) rather than a copy of the join loop, so they cover the split arithmetic, the
	/// per-chunk serialisation and the pending-set bookkeeping without needing a socket.
	/// </summary>
	public static class ChunkedPacketTests
	{
		// Mirrors LiteNetLibPacketSender/RiptidePacketSender: MAX_PAYLOAD_BYTES - header room.
		private const int CHUNK_DATA_SIZE = 1000 - 20;

		private static byte[] MakePayload(int length)
		{
			var data = new byte[length];
			for (int i = 0; i < length; i++)
				data[i] = (byte)(i * 31 + (i >> 8));

			return data;
		}

		/// <summary>
		/// Split exactly the way SendChunked does, then push every piece through a
		/// serialise/deserialise round trip so the wire format is exercised too.
		/// </summary>
		private static ChunkedPacket[] SplitAndRoundTrip(byte[] payload, int sequenceId)
		{
			int totalChunks = (payload.Length + CHUNK_DATA_SIZE - 1) / CHUNK_DATA_SIZE;
			var packets = new ChunkedPacket[totalChunks];

			for (int i = 0; i < totalChunks; i++)
			{
				int offset = i * CHUNK_DATA_SIZE;
				int length = Math.Min(CHUNK_DATA_SIZE, payload.Length - offset);
				var chunkData = new byte[length];
				Array.Copy(payload, offset, chunkData, 0, length);

				var sent = new ChunkedPacket
				{
					SequenceId = sequenceId,
					ChunkIndex = i,
					TotalChunks = totalChunks,
					ChunkData = chunkData
				};

				byte[] wire = PacketSender.SerializePacketForSending(sent);

				// Skip the 4 byte packet id the sender prepends; Deserialize starts after it.
				using var stream = new System.IO.MemoryStream(wire, 4, wire.Length - 4);
				using var reader = new System.IO.BinaryReader(stream);

				var received = new ChunkedPacket();
				received.Deserialize(reader);
				packets[i] = received;
			}

			return packets;
		}

		[UnitTest(name: "Chunking: an oversized payload survives split and reassembly", category: "Transport")]
		public static UnitTestResult OversizedPayloadRoundTrips()
		{
			ChunkedPacket.ResetPending();

			// 2600 bytes over a 980 byte chunk = 3 pieces with a deliberately short last one,
			// which is where an off-by-one in the split would show up.
			byte[] payload = MakePayload(2600);
			var chunks = SplitAndRoundTrip(payload, 9001);

			if (chunks.Length != 3)
				return UnitTestResult.Fail($"Expected 3 chunks for 2600 bytes, got {chunks.Length}");

			byte[] reassembled = null;
			for (int i = 0; i < chunks.Length; i++)
			{
				bool complete = ChunkedPacket.TryReassemble(
					chunks[i].SequenceId, chunks[i].ChunkIndex, chunks[i].TotalChunks, chunks[i].ChunkData,
					out var result);

				if (complete && i != chunks.Length - 1)
					return UnitTestResult.Fail($"Reassembly reported complete at chunk {i} of {chunks.Length}");

				if (complete)
					reassembled = result;
			}

			if (reassembled == null)
				return UnitTestResult.Fail("Reassembly never completed");

			if (reassembled.Length != payload.Length)
				return UnitTestResult.Fail($"Reassembled {reassembled.Length} bytes, expected {payload.Length}");

			for (int i = 0; i < payload.Length; i++)
			{
				if (reassembled[i] != payload[i])
					return UnitTestResult.Fail($"Payload differs at byte {i}: {reassembled[i]} != {payload[i]}");
			}

			return UnitTestResult.Pass($"{payload.Length} bytes survived {chunks.Length} chunks byte for byte");
		}

		[UnitTest(name: "Chunking: chunks arriving out of order still reassemble", category: "Transport")]
		public static UnitTestResult OutOfOrderChunksReassemble()
		{
			ChunkedPacket.ResetPending();

			// Both LAN senders can emit chunks unreliably, so arrival order is not guaranteed.
			byte[] payload = MakePayload(3500);
			var chunks = SplitAndRoundTrip(payload, 9002);

			byte[] reassembled = null;
			for (int i = chunks.Length - 1; i >= 0; i--)
			{
				if (ChunkedPacket.TryReassemble(
					chunks[i].SequenceId, chunks[i].ChunkIndex, chunks[i].TotalChunks, chunks[i].ChunkData,
					out var result))
				{
					reassembled = result;
				}
			}

			if (reassembled == null)
				return UnitTestResult.Fail("Reverse-order delivery never completed");

			if (reassembled.Length != payload.Length)
				return UnitTestResult.Fail($"Reassembled {reassembled.Length} bytes, expected {payload.Length}");

			for (int i = 0; i < payload.Length; i++)
			{
				if (reassembled[i] != payload[i])
					return UnitTestResult.Fail($"Payload differs at byte {i} after reverse-order delivery");
			}

			return UnitTestResult.Pass("Reverse-order chunks reassemble in the original order");
		}

		[UnitTest(name: "Chunking: a corrupt header is rejected, not thrown on", category: "Transport")]
		public static UnitTestResult CorruptHeaderIsRejected()
		{
			ChunkedPacket.ResetPending();

			var data = new byte[16];

			if (ChunkedPacket.TryReassemble(9100, 0, 0, data, out _))
				return UnitTestResult.Fail("TotalChunks = 0 was accepted");

			if (ChunkedPacket.TryReassemble(9101, 0, -5, data, out _))
				return UnitTestResult.Fail("A negative TotalChunks was accepted");

			if (ChunkedPacket.TryReassemble(9102, 0, int.MaxValue, data, out _))
				return UnitTestResult.Fail("An absurd TotalChunks was accepted - that sizes an allocation");

			// The one that used to throw IndexOutOfRangeException straight out of the handler.
			if (ChunkedPacket.TryReassemble(9103, 7, 3, data, out _))
				return UnitTestResult.Fail("A ChunkIndex past TotalChunks was accepted");

			if (ChunkedPacket.TryReassemble(9104, -1, 3, data, out _))
				return UnitTestResult.Fail("A negative ChunkIndex was accepted");

			if (ChunkedPacket.TryReassemble(9105, 0, 2, null, out _))
				return UnitTestResult.Fail("A null chunk body was accepted");

			return UnitTestResult.Pass("Corrupt chunk headers are rejected without throwing");
		}
	}
}
