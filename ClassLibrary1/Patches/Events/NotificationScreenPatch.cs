using HarmonyLib;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Events;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.Events
{
	[HarmonyPatch(typeof(NotificationScreen), "AddNotification")]
	public static class NotificationScreenPatch
	{
		public static void Postfix(Notification notification)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost) return;
			if (notification == null) return;

			// Avoid syncing extremely frequent or spammy notifications if necessary.
			// For now, sync all.

			// Notification.ToolTip is a delegate. We need the text.
			// Often notification.titleText is the main title.
			// notification.tooltipData might be null.

			// Let's try to extract basic info.
			string title = notification.titleText;
			string typeName = notification.Type.ToString();

			// Text is harder because it's a dynamic delegate.
			// We can try to invoke it if possible, or just send Title.
			string text = title; // Default fallback

			var packet = new NotificationPacket
			{
				Title = title,
				Text = text,
				TypeName = typeName
			};

			PacketSender.SendToAllClients(packet);
		}
	}
}
