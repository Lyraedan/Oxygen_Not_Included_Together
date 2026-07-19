using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World.SideScreen
{
	/// <summary>
	/// Patches for LogicAlarm (Automated Notifier) and AlarmSideScreen
	/// </summary>

	/// <summary>
	/// Sync LogicAlarm (Automated Notifier) settings including text fields
	/// </summary>
	[HarmonyPatch(typeof(LogicAlarm), nameof(LogicAlarm.OnCopySettings))]
	public static class LogicAlarm_OnCopySettings_Patch
	{
		public static void Postfix(LogicAlarm __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			// Sync notification type
			var packetType = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("AlarmNotificationType"),
				Value = (int)__instance.notificationType,
				ConfigType = BuildingConfigType.Float
			};

			// Sync pause on notify
			var packetPause = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("AlarmPauseOnNotify"),
				Value = __instance.pauseOnNotify ? 1f : 0f,
				ConfigType = BuildingConfigType.Boolean
			};

			// Sync zoom on notify
			var packetZoom = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("AlarmZoomOnNotify"),
				Value = __instance.zoomOnNotify ? 1f : 0f,
				ConfigType = BuildingConfigType.Boolean
			};

			// Sync notification name (text)
			var packetName = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("AlarmNotificationName"),
				Value = 0,
				ConfigType = BuildingConfigType.String,
				StringValue = __instance.notificationName ?? ""
			};

			// Sync notification tooltip (text)
			var packetTooltip = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("AlarmNotificationTooltip"),
				Value = 0,
				ConfigType = BuildingConfigType.String,
				StringValue = __instance.notificationTooltip ?? ""
			};

			if (MultiplayerSession.IsHost)
			{
				PacketSender.SendToAllClients(packetType);
				PacketSender.SendToAllClients(packetPause);
				PacketSender.SendToAllClients(packetZoom);
				PacketSender.SendToAllClients(packetName);
				PacketSender.SendToAllClients(packetTooltip);
			}
			else
			{
				PacketSender.SendToHost(packetType);
				PacketSender.SendToHost(packetPause);
				PacketSender.SendToHost(packetZoom);
				PacketSender.SendToHost(packetName);
				PacketSender.SendToHost(packetTooltip);
			}
		}
	}

	/// <summary>
	/// Sync AlarmSideScreen.OnEndEditName (when user edits notification name)
	/// </summary>
	[HarmonyPatch(typeof(AlarmSideScreen), nameof(AlarmSideScreen.OnEndEditName))]
	public static class AlarmSideScreen_OnEndEditName_Patch
	{
		public static void Postfix(AlarmSideScreen __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;
			if (__instance.targetAlarm == null) return;

			var identity = __instance.targetAlarm.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.targetAlarm.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("AlarmName"),
				ConfigType = BuildingConfigType.String,
				StringValue = __instance.targetAlarm.notificationName ?? ""
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	/// <summary>
	/// Sync AlarmSideScreen.OnEndEditTooltip (when user edits notification tooltip)
	/// </summary>
	[HarmonyPatch(typeof(AlarmSideScreen), nameof(AlarmSideScreen.OnEndEditTooltip))]
	public static class AlarmSideScreen_OnEndEditTooltip_Patch
	{
		public static void Postfix(AlarmSideScreen __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;
			if (__instance.targetAlarm == null) return;

			var identity = __instance.targetAlarm.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.targetAlarm.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("AlarmTooltip"),
				ConfigType = BuildingConfigType.String,
				StringValue = __instance.targetAlarm.notificationTooltip ?? ""
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	/// <summary>
	/// Sync AlarmSideScreen.TogglePause
	/// </summary>
	[HarmonyPatch(typeof(AlarmSideScreen), nameof(AlarmSideScreen.TogglePause))]
	public static class AlarmSideScreen_TogglePause_Patch
	{
		public static void Postfix(AlarmSideScreen __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;
			if (__instance.targetAlarm == null) return;

			var identity = __instance.targetAlarm.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.targetAlarm.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("AlarmPause"),
				Value = __instance.targetAlarm.pauseOnNotify ? 1f : 0f,
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	/// <summary>
	/// Sync AlarmSideScreen.ToggleZoom
	/// </summary>
	[HarmonyPatch(typeof(AlarmSideScreen), nameof(AlarmSideScreen.ToggleZoom))]
	public static class AlarmSideScreen_ToggleZoom_Patch
	{
		public static void Postfix(AlarmSideScreen __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;
			if (__instance.targetAlarm == null) return;

			var identity = __instance.targetAlarm.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.targetAlarm.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("AlarmZoom"),
				Value = __instance.targetAlarm.zoomOnNotify ? 1f : 0f,
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	/// <summary>
	/// Sync AlarmSideScreen.SelectType (notification type selection)
	/// </summary>
	[HarmonyPatch(typeof(AlarmSideScreen), nameof(AlarmSideScreen.SelectType))]
	public static class AlarmSideScreen_SelectType_Patch
	{
		public static void Postfix(AlarmSideScreen __instance, NotificationType type)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;
			if (__instance.targetAlarm == null) return;

			var identity = __instance.targetAlarm.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.targetAlarm.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("AlarmType"),
				Value = (int)type,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	/// <summary>
	/// Force AlarmSideScreen to refresh from component values when SetTarget is called.
	/// </summary>
	[HarmonyPatch(typeof(AlarmSideScreen), nameof(AlarmSideScreen.SetTarget))]
	public static class AlarmSideScreen_SetTarget_Patch
	{
		public static void Postfix(AlarmSideScreen __instance, GameObject target)
		{
			using var _ = Profiler.Scope();

			if (__instance.targetAlarm == null) return;

			// Force update visuals from current component values
			__instance.UpdateVisuals();
			__instance.RefreshToggles();
		}
	}
}
