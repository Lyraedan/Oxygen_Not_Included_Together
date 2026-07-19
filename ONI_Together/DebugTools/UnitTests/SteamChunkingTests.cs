#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SteamChunkingTests
	{
		[UnitTest(name: "Steam chunking stays below the single-message ceiling", category: "Networking")]
		public static UnitTestResult ChunksStayBelowSteamCeiling()
		{
			byte[] payload = Payload(Utils.MaxSteamNetworkingSocketsMessageSizeSend + 4096);
			var chunks = new List<ChunkedPacket>();
			bool sent = ChunkedPacket.TrySendSerializedChunks(
				payload,
				Utils.MaxSteamNetworkingSocketsMessageSizeSend,
				chunk =>
				{
					chunks.Add(chunk);
					return true;
				});
			if (!sent || chunks.Count < 2)
				return UnitTestResult.Fail("Oversized Steam payload was not split");
			if (chunks.Any(chunk =>
				    ChunkedPacket.SerializedEnvelopeBytes + chunk.ChunkData.Length
				    >= Utils.MaxSteamNetworkingSocketsMessageSizeSend))
				return UnitTestResult.Fail("A Steam chunk reached or exceeded the exclusive ceiling");
			if (chunks.SelectMany(chunk => chunk.ChunkData).SequenceEqual(payload) == false)
				return UnitTestResult.Fail("Steam chunk planning changed payload bytes");
			return UnitTestResult.Pass("Every Steam chunk is strictly below 512 KiB");
		}

		[UnitTest(name: "Steam chunking stops on transport rejection", category: "Networking")]
		public static UnitTestResult ChunkSendFailureIsReturned()
		{
			int calls = 0;
			bool sent = ChunkedPacket.TrySendSerializedChunks(
				Payload(Utils.MaxSteamNetworkingSocketsMessageSizeSend * 2),
				Utils.MaxSteamNetworkingSocketsMessageSizeSend,
				_ => ++calls < 2);
			return !sent && calls == 2
				? UnitTestResult.Pass("The first rejected chunk stops and fails the send")
				: UnitTestResult.Fail("Chunk rejection was hidden or later chunks were still sent");
		}

		[UnitTest(name: "Steam chunk reassembly is once-only and session isolated", category: "Networking")]
		public static UnitTestResult ReassemblyIsOnceOnlyAndSessionIsolated()
		{
			ChunkedPacket.ResetSessionState();
			try
			{
				byte[] payload = Payload(Utils.MaxSteamNetworkingSocketsMessageSizeSend + 73);
				var chunks = new List<ChunkedPacket>();
				if (!ChunkedPacket.TrySendSerializedChunks(
					    payload,
					    Utils.MaxSteamNetworkingSocketsMessageSizeSend,
					    chunk => { chunks.Add(chunk); return true; }))
					return UnitTestResult.Fail("Could not plan Steam chunks");

				var firstSession = new DispatchContext(55, true, 7, 101);
				var nextSession = new DispatchContext(55, true, 7, 102);
				var nextConnection = new DispatchContext(55, true, 8, 101);
				if (!CompletesExactlyOnce(chunks, firstSession, payload)
				    || !CompletesExactlyOnce(chunks, nextSession, payload)
				    || !CompletesExactlyOnce(chunks, nextConnection, payload))
					return UnitTestResult.Fail(
						"Chunk completion was duplicated or leaked across session/connection boundaries");
				return UnitTestResult.Pass(
					"Reassembly dispatches once per session and connection generation");
			}
			finally
			{
				ChunkedPacket.ResetSessionState();
			}
		}

		private static bool CompletesExactlyOnce(
			IReadOnlyList<ChunkedPacket> chunks,
			DispatchContext context,
			byte[] expected)
		{
			int completions = 0;
			byte[] assembled = null;
			foreach (ChunkedPacket chunk in chunks)
			{
				if (!ChunkedPacket.TryAcceptChunk(chunk, context, out byte[] data, out _))
					continue;
				completions++;
				assembled = data;
			}
			foreach (ChunkedPacket chunk in chunks)
				if (ChunkedPacket.TryAcceptChunk(chunk, context, out _, out _))
					completions++;
			return completions == 1 && assembled != null && assembled.SequenceEqual(expected);
		}

		private static byte[] Payload(int length)
		{
			var payload = new byte[length];
			for (int index = 0; index < payload.Length; index++)
				payload[index] = (byte)(index * 31 + 7);
			return payload;
		}
	}
}
#endif
