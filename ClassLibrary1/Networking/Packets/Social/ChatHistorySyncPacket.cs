using ONI_MP.Menus;
using System.Collections.Generic;
using System.IO;
using ONI_MP.Networking.Packets.Architecture;
using Shared.Profiling;

namespace ONI_MP.Networking.Packets.Social
{
	public class ChatHistorySyncPacket : IPacket
	{
		public List<ChatScreen.PendingMessage> Messages = new List<ChatScreen.PendingMessage>();

		public ChatHistorySyncPacket()
		{
		}

		public ChatHistorySyncPacket(List<ChatScreen.PendingMessage> messages)
		{
			using var _ = Profiler.Scope();

			Messages = messages;
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(Messages.Count);
			foreach (var msg in Messages)
			{
				writer.Write(msg.timestamp);
				writer.Write(msg.message);
			}
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			int count = reader.ReadInt32();
			Messages = new List<ChatScreen.PendingMessage>(count);
			for (int i = 0; i < count; i++)
			{
				Messages.Add(new ChatScreen.PendingMessage
				{
					timestamp = reader.ReadInt64(),
					message = reader.ReadString()
				});
			}
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (ChatScreen.Instance != null)
				ChatScreen.Instance.ClearMessages();

			var initMsg = ChatScreen.GeneratePendingMessage(STRINGS.UI.MP_CHATWINDOW.CHAT_INITIALIZED);
			ChatScreen.QueueMessage(initMsg);

			foreach (var msg in Messages)
				ChatScreen.QueueMessage(msg);
		}
	}
}
