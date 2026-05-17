using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Events
{
	// Host -> Client: Syncs notifications like "Research Complete", "Starvation", etc.
	public class NotificationPacket : IPacket
	{
		public string Title;
		public string Text;
		// We can't easily sync the "Type" enum because it might rely on game assembly enums not visible or complex.
		// We'll send it as a string or try to cast if NotificationType is available.
		// NotificationType is an enum in specific namespace. Let's use string for now or map common ones.
		// On second thought, sending the exact enum int is better if we assume both have same code.
		public string TypeName;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(Title ?? string.Empty);
			writer.Write(Text ?? string.Empty);
			writer.Write(TypeName ?? "Bad");
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Title = reader.ReadString();
			Text = reader.ReadString();
			TypeName = reader.ReadString();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost) return;
			Apply();
		}

		private void Apply()
		{
			using var _ = Profiler.Scope();

			// Create a local notification
			// Notification(string title, NotificationType type, HashedString? tooltip = null, object tooltip_data = null, bool expires = true, float delay = 0f, Notification.ClickCallback custom_click_callback = null, object custom_click_data = null, Transform click_focus = null, bool volume_attenuation = true)

			NotificationType type = NotificationType.Bad;
			try
			{
				type = (NotificationType)System.Enum.Parse(typeof(NotificationType), TypeName);
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[NotificationPacket] Error parsing notification type: {ex}");
				type = NotificationType.Bad;
			}

			var notification = new Notification(Title, type, (List<Notification> n, object d) => Text, null, true, 0f, null, null, null, true, false, false);

			// Add to screen
			// ManagementScreen.Instance might have it? Or NotificationScreen.Instance.
			if (NotificationScreen.Instance != null)
			{
				NotificationScreen.Instance.AddNotification(notification);
			}
		}
	}
}
