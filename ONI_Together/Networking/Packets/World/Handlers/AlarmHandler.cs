using UnityEngine;
using ONI_Together.DebugTools;
using Shared;
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
			NetworkingHash.ForConfigKey("AlarmName"),
			NetworkingHash.ForConfigKey("AlarmTooltip"),
			NetworkingHash.ForConfigKey("AlarmPause"),
			NetworkingHash.ForConfigKey("AlarmZoom"),
			NetworkingHash.ForConfigKey("AlarmType"),
			// Legacy hash names (from OnCopySettings patch)
			NetworkingHash.ForConfigKey("AlarmNotificationName"),
			NetworkingHash.ForConfigKey("AlarmNotificationTooltip"),
			NetworkingHash.ForConfigKey("AlarmNotificationType"),
			NetworkingHash.ForConfigKey("AlarmPauseOnNotify"),
			NetworkingHash.ForConfigKey("AlarmZoomOnNotify"),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			var alarm = go.GetComponent<LogicAlarm>();
			if (alarm == null) return false;

			int hash = packet.ConfigHash;

			// Name (both old and new hash)
			if (hash == NetworkingHash.ForConfigKey("AlarmName") || hash == NetworkingHash.ForConfigKey("AlarmNotificationName"))
			{
				if (packet.ConfigType == BuildingConfigType.String
				    && (packet.StringValue?.Length ?? 0) <= 256)
				{
					alarm.notificationName = packet.StringValue ?? "";
					alarm.UpdateNotification(true);
					//DebugConsole.Log($"[AlarmHandler] Set notificationName='{packet.StringValue}' on {go.name}");
					return true;
				}
			}

			// Tooltip (both old and new hash)
			if (hash == NetworkingHash.ForConfigKey("AlarmTooltip") || hash == NetworkingHash.ForConfigKey("AlarmNotificationTooltip"))
			{
				if (packet.ConfigType == BuildingConfigType.String)
				{
					alarm.notificationTooltip = packet.StringValue ?? "";
					alarm.UpdateNotification(true);
					//DebugConsole.Log($"[AlarmHandler] Set notificationTooltip='{packet.StringValue}' on {go.name}");
					return true;
				}
			}

			// Pause (both old and new hash)
			if (hash == NetworkingHash.ForConfigKey("AlarmPause") || hash == NetworkingHash.ForConfigKey("AlarmPauseOnNotify"))
			{
				if (packet.ConfigType != BuildingConfigType.Boolean
				    || !BuildingConfigPacket.IsBooleanValue(packet.Value))
					return false;
				alarm.pauseOnNotify = packet.Value > 0.5f;
				alarm.UpdateNotification(true);
				//DebugConsole.Log($"[AlarmHandler] Set pauseOnNotify={alarm.pauseOnNotify} on {go.name}");
				return true;
			}

			// Zoom (both old and new hash)
			if (hash == NetworkingHash.ForConfigKey("AlarmZoom") || hash == NetworkingHash.ForConfigKey("AlarmZoomOnNotify"))
			{
				if (packet.ConfigType != BuildingConfigType.Boolean
				    || !BuildingConfigPacket.IsBooleanValue(packet.Value))
					return false;
				alarm.zoomOnNotify = packet.Value > 0.5f;
				alarm.UpdateNotification(true);
				//DebugConsole.Log($"[AlarmHandler] Set zoomOnNotify={alarm.zoomOnNotify} on {go.name}");
				return true;
			}

			// Type (both old and new hash)
			if (hash == NetworkingHash.ForConfigKey("AlarmType") || hash == NetworkingHash.ForConfigKey("AlarmNotificationType"))
			{
				if (packet.ConfigType != BuildingConfigType.Float
				    || !BuildingConfigPacket.IsIntegralValue(packet.Value)
				    || !System.Enum.IsDefined(typeof(NotificationType), (int)packet.Value))
					return false;
				alarm.notificationType = (NotificationType)(int)packet.Value;
				alarm.UpdateNotification(true);
				//DebugConsole.Log($"[AlarmHandler] Set notificationType={alarm.notificationType} on {go.name}");
				return true;
			}

			return false;
		}
	}
}
