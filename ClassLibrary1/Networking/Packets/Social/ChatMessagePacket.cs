using ONI_MP.Misc;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.Architecture;
using ONI_MP.UI;
using Steamworks;
using System;
using System.IO;
using Shared.Profiling;
using UnityEngine;
using ONI_MP.Menus;
using ONI_MP.Networking;

namespace ONI_MP.Networking.Packets.Social
{
	public class ChatMessagePacket : IPacket
	{
		public ulong SenderId;
		public string Message;
		public Color PlayerColor;
		public long Timestamp;
		public string SenderName;

		public ChatMessagePacket()
		{
		}

		public ChatMessagePacket(string message)
		{
			using var _ = Profiler.Scope();

			SenderId = MultiplayerSession.LocalUserID;
            SenderName = Utils.GetLocalPlayerName();
            Message = message;
			PlayerColor = CursorManager.Instance.color;
			Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(SenderId);
			writer.Write(SenderName);
			writer.Write(Message);
			writer.Write(PlayerColor.r);
			writer.Write(PlayerColor.g);
			writer.Write(PlayerColor.b);
			writer.Write(PlayerColor.a);
			writer.Write(Timestamp);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			SenderId = reader.ReadUInt64();
			SenderName = reader.ReadString();
			Message = reader.ReadString();
			float r = reader.ReadSingle();
			float g = reader.ReadSingle();
			float b = reader.ReadSingle();
			float a = reader.ReadSingle();
			PlayerColor = new Color(r, g, b, a);
			Timestamp = reader.ReadInt64();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (SenderId == MultiplayerSession.LocalUserID)
				return;

            string senderName = SenderName;
            if (NetworkConfig.IsSteamConfig() && SteamFriends.HasFriend(SenderId.AsCSteamID(), EFriendFlags.k_EFriendFlagImmediate))
			{
				// Update the sender name to what we have them named as on our friends list
                senderName = SteamFriends.GetFriendPersonaName(SenderId.AsCSteamID());
            }
			string colorHex = ColorUtility.ToHtmlStringRGB(PlayerColor);
			ChatScreen.PendingMessage message = new ChatScreen.PendingMessage()
			{
				timestamp = Timestamp,
				message = string.Format("<color=#{0}>{1}:</color> {2}", colorHex, senderName, Message)
			};
			ChatScreen.QueueMessage(message);

			if (MultiplayerSession.IsHost)
			{
				// Broadcast the chat to all other clients except sender and host
				PacketSender.SendToAllOtherPeers(this);
			}
		}
	}
}
