using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Social;
using ONI_Together.UI;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class ChatPacketTests
	{
		[UnitTest(name: "Chat messages reject invalid wire fields", category: "Networking", liveSafe: true)]
		public static UnitTestResult RejectsInvalidWireFields()
		{
			if (!Rejects(new ChatMessagePacket(), writer => WriteChat(writer, 0, "player", "hello", 1, 1, 1, 1, 1)))
				return UnitTestResult.Fail("ChatMessagePacket accepted sender id zero");
			if (!Rejects(new ChatMessagePacket(), writer => WriteChat(writer, 1, "player", "hello", float.NaN, 1, 1, 1, 1)))
				return UnitTestResult.Fail("ChatMessagePacket accepted a non-finite color");
			if (!Rejects(new ChatMessagePacket(), writer => WriteChat(writer, 1, "player", "hello", 1.1f, 1, 1, 1, 1)))
				return UnitTestResult.Fail("ChatMessagePacket accepted an out-of-range color");
			if (!Rejects(new ChatMessagePacket(), writer => WriteChat(writer, 1, "player", "hello", 1, 1, 1, 1, -1)))
				return UnitTestResult.Fail("ChatMessagePacket accepted an invalid timestamp");
			if (!Rejects(new ChatMessagePacket(), writer => WriteChat(writer, 1, "player", "hello", 1, 1, 1, 1, 253402300800000)))
				return UnitTestResult.Fail("ChatMessagePacket accepted a timestamp beyond DateTimeOffset.MaxValue");

			return UnitTestResult.Pass("Invalid chat wire fields are rejected");
		}

		[UnitTest(name: "Chat strings use symmetric UTF-8 wire limits", category: "Networking", liveSafe: true)]
		public static UnitTestResult EnforcesUtf8WireLimits()
		{
			var valid = new ChatMessagePacket
			{
				SenderId = 7,
				SenderName = new string('名', 42),
				Message = string.Concat(System.Linq.Enumerable.Repeat("🙂", 256)),
				PlayerColor = new Color(0, 0.5f, 1, 1),
				Timestamp = 1
			};
			byte[] bytes = Serialize(valid);
			if (bytes.Length > ChatMessagePacket.MaxSerializedBytes)
				return UnitTestResult.Fail($"Valid chat message used {bytes.Length} wire bytes");
			var roundTrip = RoundTrip(valid);
			if (roundTrip.SenderName != valid.SenderName || roundTrip.Message != valid.Message)
				return UnitTestResult.Fail("Valid multi-byte chat fields changed during roundtrip");

			valid.SenderName += "名";
			if (!SerializeRejects(valid))
				return UnitTestResult.Fail("Serialize accepted a sender name over the UTF-8 limit");
			if (!Rejects(new ChatMessagePacket(), writer => WriteChat(writer, 7, valid.SenderName, "hello", 1, 1, 1, 1, 1)))
				return UnitTestResult.Fail("Deserialize accepted a sender name over the UTF-8 limit");

			valid.SenderName = "player";
			valid.Message += "🙂";
			if (!SerializeRejects(valid))
				return UnitTestResult.Fail("Serialize accepted a message over the UTF-8 limit");
			if (!Rejects(new ChatMessagePacket(), writer => WriteChat(writer, 7, "player", valid.Message, 1, 1, 1, 1, 1)))
				return UnitTestResult.Fail("Deserialize accepted a message over the UTF-8 limit");

			return UnitTestResult.Pass("Chat strings are bounded symmetrically by UTF-8 wire bytes");
		}

		[UnitTest(name: "Chat history keeps a bounded latest slice", category: "Networking", liveSafe: true)]
		public static UnitTestResult KeepsBoundedLatestHistory()
		{
			var source = new List<ChatScreen.PendingMessage>();
			for (int i = 0; i < ChatHistorySyncPacket.MaxMessageCount + 40; i++)
				source.Add(new ChatScreen.PendingMessage { timestamp = i + 1, message = $"message-{i:D3}-" + new string('x', 80) });

			var packet = new ChatHistorySyncPacket(source);
			byte[] bytes = Serialize(packet);
			if (packet.Messages.Count > ChatHistorySyncPacket.MaxMessageCount || bytes.Length > ChatHistorySyncPacket.MaxSerializedBytes)
				return UnitTestResult.Fail($"History exceeded bounds: count={packet.Messages.Count}, bytes={bytes.Length}");
			if (packet.Messages.Count == 0 || packet.Messages[packet.Messages.Count - 1].timestamp != source[source.Count - 1].timestamp)
				return UnitTestResult.Fail("History did not retain the newest message");
			long expectedFirst = source[source.Count - packet.Messages.Count].timestamp;
			if (packet.Messages[0].timestamp != expectedFirst)
				return UnitTestResult.Fail("History is not a deterministic contiguous latest slice");
			source.Clear();
			if (packet.Messages.Count == 0)
				return UnitTestResult.Fail("History constructor retained the caller's mutable list");

			var roundTrip = RoundTrip(packet);
			if (roundTrip.Messages.Count != packet.Messages.Count || roundTrip.Messages[0].timestamp != expectedFirst)
				return UnitTestResult.Fail("Bounded history changed during roundtrip");

			var captured = new List<ChatScreen.PendingMessage>();
			foreach (var message in packet.Messages) ChatHistorySyncPacket.AppendBounded(captured, message);
			for (int i = 0; i < 80; i++)
				ChatHistorySyncPacket.AppendBounded(captured,
					new ChatScreen.PendingMessage { timestamp = 1000 + i, message = new string('y', 100) });
			if (captured.Count > ChatHistorySyncPacket.MaxMessageCount || Serialize(new ChatHistorySyncPacket(captured)).Length > ChatHistorySyncPacket.MaxSerializedBytes)
				return UnitTestResult.Fail("Captured chat history did not evict oldest entries");
			if (captured[captured.Count - 1].timestamp != 1079)
				return UnitTestResult.Fail("Captured chat history evicted a newest entry");
			return UnitTestResult.Pass("History is a copied, bounded latest slice");
		}

		[UnitTest(name: "Chat history rejects mutated or oversized wire state", category: "Networking", liveSafe: true)]
		public static UnitTestResult RejectsInvalidHistoryState()
		{
			var packet = new ChatHistorySyncPacket();
			for (int i = 0; i <= ChatHistorySyncPacket.MaxMessageCount; i++)
				packet.Messages.Add(new ChatScreen.PendingMessage { timestamp = i + 1, message = "x" });
			if (!SerializeRejects(packet))
				return UnitTestResult.Fail("Serialize accepted a mutated oversized history count");
			if (!SerializeRejects(new ChatHistorySyncPacket
			{
				Messages = new List<ChatScreen.PendingMessage>
				{
					new ChatScreen.PendingMessage { timestamp = -1, message = "invalid" }
				}
			})) return UnitTestResult.Fail("Serialize accepted an invalid history timestamp");
			if (!Rejects(new ChatHistorySyncPacket(), writer =>
			{
				writer.Write(1);
				writer.Write(-1L);
				writer.Write("invalid");
			})) return UnitTestResult.Fail("Deserialize accepted an invalid history timestamp");

			if (!Rejects(new ChatHistorySyncPacket(), writer =>
			{
				writer.Write(2);
				for (int i = 0; i < 2; i++)
				{
					writer.Write((long)i + 1);
					writer.Write(new string('x', ChatHistorySyncPacket.MaxMessageUtf8Bytes));
				}
			})) return UnitTestResult.Fail("Deserialize accepted history beyond the total wire-byte limit");

			return UnitTestResult.Pass("History validates count and total wire bytes on both paths");
		}

		[UnitTest(name: "Chat display escapes user rich text", category: "UI", liveSafe: true)]
		public static UnitTestResult EscapesUserRichText()
		{
			string rendered = ChatMessagePacket.FormatDisplayMessage(
				"<size=99>name</size>", new Color(1, 1, 1, 1), "<color=red>owned</color>");
			if (rendered.Contains("<size=99>") || rendered.Contains("<color=red>"))
				return UnitTestResult.Fail("User-controlled rich-text tags reached the renderer");
			if (!rendered.StartsWith("<color=#FFFFFF>") || !rendered.Contains("&lt;color=red&gt;owned&lt;/color&gt;"))
				return UnitTestResult.Fail("System color markup or escaped user text was not preserved");
			return UnitTestResult.Pass("Only system-generated chat markup remains active");
		}

		private static void WriteChat(BinaryWriter writer, ulong senderId, string senderName, string message,
			float r, float g, float b, float a, long timestamp)
		{
			writer.Write(senderId);
			writer.Write(senderName);
			writer.Write(message);
			writer.Write(r);
			writer.Write(g);
			writer.Write(b);
			writer.Write(a);
			writer.Write(timestamp);
		}

		private static bool Rejects(IPacket packet, Action<BinaryWriter> write)
		{
			try
			{
				using var stream = new MemoryStream();
				using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
					write(writer);
				stream.Position = 0;
				using var reader = new BinaryReader(stream);
				packet.Deserialize(reader);
				return false;
			}
			catch (InvalidDataException)
			{
				return true;
			}
		}

		private static bool SerializeRejects(IPacket packet)
		{
			try { Serialize(packet); return false; }
			catch (InvalidDataException) { return true; }
		}

		private static byte[] Serialize(IPacket packet)
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
				packet.Serialize(writer);
			return stream.ToArray();
		}

		private static T RoundTrip<T>(T packet) where T : IPacket, new()
		{
			byte[] bytes = Serialize(packet);
			using var stream = new MemoryStream(bytes);
			using var reader = new BinaryReader(stream, Encoding.UTF8, true);
			var copy = new T();
			copy.Deserialize(reader);
			return copy;
		}
	}
}
