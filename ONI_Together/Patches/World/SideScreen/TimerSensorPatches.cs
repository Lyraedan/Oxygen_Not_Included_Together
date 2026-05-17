using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World.SideScreen
{
	/// <summary>
	/// Patches for timer and cycle sensors (LogicTimerSensor, LogicTimeOfDaySensor, CritterSensor)
	/// </summary>

	/// <summary>
	/// Sync critter sensor checkbox toggles
	/// </summary>
	[HarmonyPatch(typeof(CritterSensorSideScreen), nameof(CritterSensorSideScreen.ToggleCritters))]
	public static class CritterSensorSideScreen_ToggleCritters_Patch
	{
		public static void Postfix(CritterSensorSideScreen __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (__instance.targetSensor == null) return;

			var identity = __instance.targetSensor.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.targetSensor.gameObject),
				ConfigHash = "CritterCountCritters".GetHashCode(),
				Value = __instance.targetSensor.countCritters ? 1f : 0f,
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	[HarmonyPatch(typeof(CritterSensorSideScreen), nameof(CritterSensorSideScreen.ToggleEggs))]
	public static class CritterSensorSideScreen_ToggleEggs_Patch
	{
		public static void Postfix(CritterSensorSideScreen __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (__instance.targetSensor == null) return;

			var identity = __instance.targetSensor.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.targetSensor.gameObject),
				ConfigHash = "CritterCountEggs".GetHashCode(),
				Value = __instance.targetSensor.countEggs ? 1f : 0f,
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	/// <summary>
	/// Sync timer sensor settings via copy/paste
	/// </summary>
	[HarmonyPatch(typeof(LogicTimerSensor), nameof(LogicTimerSensor.OnCopySettings))]
	public static class LogicTimerSensor_OnCopySettings_Patch
	{
		public static void Postfix(LogicTimerSensor __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			// Sync both on and off durations
			var packetOn = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "TimerOnDuration".GetHashCode(),
				Value = __instance.onDuration,
				ConfigType = BuildingConfigType.Float
			};
			var packetOff = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "TimerOffDuration".GetHashCode(),
				Value = __instance.offDuration,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost)
			{
				PacketSender.SendToAllClients(packetOn);
				PacketSender.SendToAllClients(packetOff);
			}
			else
			{
				PacketSender.SendToHost(packetOn);
				PacketSender.SendToHost(packetOff);
			}
		}
	}

	/// <summary>
	/// Sync timer sensor settings via direct slider changes
	/// </summary>
	[HarmonyPatch(typeof(TimerSideScreen), nameof(TimerSideScreen.ChangeSetting))]
	public static class TimerSideScreen_ChangeSetting_Patch
	{
		public static void Postfix(TimerSideScreen __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;
			if (__instance.targetTimedSwitch == null) return;

			var identity = __instance.targetTimedSwitch.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			// Sync both on and off durations
			var packetOn = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.targetTimedSwitch.gameObject),
				ConfigHash = "TimerOnDuration".GetHashCode(),
				Value = __instance.targetTimedSwitch.onDuration,
				ConfigType = BuildingConfigType.Float
			};
			var packetOff = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.targetTimedSwitch.gameObject),
				ConfigHash = "TimerOffDuration".GetHashCode(),
				Value = __instance.targetTimedSwitch.offDuration,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost)
			{
				PacketSender.SendToAllClients(packetOn);
				PacketSender.SendToAllClients(packetOff);
			}
			else
			{
				PacketSender.SendToHost(packetOn);
				PacketSender.SendToHost(packetOff);
			}
		}
	}

	/// <summary>
	/// Sync LogicTimeOfDaySensor via OnCopySettings (cycle sensor)
	/// </summary>
	[HarmonyPatch(typeof(LogicTimeOfDaySensor), nameof(LogicTimeOfDaySensor.OnCopySettings))]
	public static class LogicTimeOfDaySensor_OnCopySettings_Patch
	{
		public static void Postfix(LogicTimeOfDaySensor __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packetStart = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "StartTime".GetHashCode(),
				Value = __instance.startTime,
				ConfigType = BuildingConfigType.Float
			};
			var packetDuration = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "Duration".GetHashCode(),
				Value = __instance.duration,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost)
			{
				PacketSender.SendToAllClients(packetStart);
				PacketSender.SendToAllClients(packetDuration);
			}
			else
			{
				PacketSender.SendToHost(packetStart);
				PacketSender.SendToHost(packetDuration);
			}
		}
	}

	/// <summary>
	/// Sync TimeRangeSideScreen.ChangeSetting (cycle sensor slider changes)
	/// </summary>
	[HarmonyPatch(typeof(TimeRangeSideScreen), nameof(TimeRangeSideScreen.ChangeSetting))]
	public static class TimeRangeSideScreen_ChangeSetting_Patch
	{
		public static void Postfix(TimeRangeSideScreen __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;
			if (__instance.targetTimedSwitch == null) return;

			var identity = __instance.targetTimedSwitch.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packetStart = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.targetTimedSwitch.gameObject),
				ConfigHash = "StartTime".GetHashCode(),
				Value = __instance.startTime.value,
				ConfigType = BuildingConfigType.Float
			};
			var packetDuration = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.targetTimedSwitch.gameObject),
				ConfigHash = "Duration".GetHashCode(),
				Value = __instance.duration.value,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost)
			{
				PacketSender.SendToAllClients(packetStart);
				PacketSender.SendToAllClients(packetDuration);
			}
			else
			{
				PacketSender.SendToHost(packetStart);
				PacketSender.SendToHost(packetDuration);
			}
		}
	}

	/// <summary>
	/// Force TimeRangeSideScreen to refresh from component values when SetTarget is called.
	/// </summary>
	[HarmonyPatch(typeof(TimeRangeSideScreen), nameof(TimeRangeSideScreen.SetTarget))]
	public static class TimeRangeSideScreen_SetTarget_Patch
	{
		public static void Postfix(TimeRangeSideScreen __instance, GameObject target)
		{
			using var _ = Profiler.Scope();

			if (__instance.targetTimedSwitch == null) return;

			// Force update sliders from current component values
			__instance.startTime.value = __instance.targetTimedSwitch.startTime;
			__instance.duration.value = __instance.targetTimedSwitch.duration;
			__instance.ChangeSetting();
		}
	}
}
