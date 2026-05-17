using UnityEngine;
using ONI_Together.DebugTools;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>
	/// Handles LogicAlarm (Automated Notifier) buildings.
	/// </summary>
	public class AlarmHandler : IBuildingConfigHandler
	{
		private static readonly int[] _hashes = new int[]
		{
			// New hash names (from side screen patches)
			"AlarmName".GetHashCode(),
			"AlarmTooltip".GetHashCode(),
			"AlarmPause".GetHashCode(),
			"AlarmZoom".GetHashCode(),
			"AlarmType".GetHashCode(),
			// Legacy hash names (from OnCopySettings patch)
			"AlarmNotificationName".GetHashCode(),
			"AlarmNotificationTooltip".GetHashCode(),
			"AlarmNotificationType".GetHashCode(),
			"AlarmPauseOnNotify".GetHashCode(),
			"AlarmZoomOnNotify".GetHashCode(),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			var alarm = go.GetComponent<LogicAlarm>();
			if (alarm == null) return false;

			int hash = packet.ConfigHash;

			// Name (both old and new hash)
			if (hash == "AlarmName".GetHashCode() || hash == "AlarmNotificationName".GetHashCode())
			{
				if (packet.ConfigType == BuildingConfigType.String && !string.IsNullOrEmpty(packet.StringValue))
				{
					alarm.notificationName = packet.StringValue;
					alarm.UpdateNotification(true);
					DebugConsole.Log($"[AlarmHandler] Set notificationName='{packet.StringValue}' on {go.name}");
					return true;
				}
			}

			// Tooltip (both old and new hash)
			if (hash == "AlarmTooltip".GetHashCode() || hash == "AlarmNotificationTooltip".GetHashCode())
			{
				if (packet.ConfigType == BuildingConfigType.String)
				{
					alarm.notificationTooltip = packet.StringValue ?? "";
					alarm.UpdateNotification(true);
					DebugConsole.Log($"[AlarmHandler] Set notificationTooltip='{packet.StringValue}' on {go.name}");
					return true;
				}
			}

			// Pause (both old and new hash)
			if (hash == "AlarmPause".GetHashCode() || hash == "AlarmPauseOnNotify".GetHashCode())
			{
				alarm.pauseOnNotify = packet.Value > 0.5f;
				alarm.UpdateNotification(true);
				DebugConsole.Log($"[AlarmHandler] Set pauseOnNotify={alarm.pauseOnNotify} on {go.name}");
				return true;
			}

			// Zoom (both old and new hash)
			if (hash == "AlarmZoom".GetHashCode() || hash == "AlarmZoomOnNotify".GetHashCode())
			{
				alarm.zoomOnNotify = packet.Value > 0.5f;
				alarm.UpdateNotification(true);
				DebugConsole.Log($"[AlarmHandler] Set zoomOnNotify={alarm.zoomOnNotify} on {go.name}");
				return true;
			}

			// Type (both old and new hash)
			if (hash == "AlarmType".GetHashCode() || hash == "AlarmNotificationType".GetHashCode())
			{
				alarm.notificationType = (NotificationType)(int)packet.Value;
				alarm.UpdateNotification(true);
				DebugConsole.Log($"[AlarmHandler] Set notificationType={alarm.notificationType} on {go.name}");
				return true;
			}

			return false;
		}
	}
}
