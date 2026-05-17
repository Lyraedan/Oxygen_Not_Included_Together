using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World.SideScreen
{
	/// <summary>
	/// Patches for IActivationRangeTarget buildings (SmartReservoir, MassageTable, ActiveRangeSideScreen)
	/// </summary>

	/// <summary>
	/// Sync SmartReservoir activation thresholds via OnCopySettings
	/// </summary>
	[HarmonyPatch(typeof(SmartReservoir), nameof(SmartReservoir.OnCopySettings))]
	public static class SmartReservoir_OnCopySettings_Patch
	{
		public static void Postfix(SmartReservoir __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packetActivate = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "SmartReservoirActivate".GetHashCode(),
				Value = __instance.activateValue,
				ConfigType = BuildingConfigType.Float
			};
			var packetDeactivate = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "SmartReservoirDeactivate".GetHashCode(),
				Value = __instance.deactivateValue,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost)
			{
				PacketSender.SendToAllClients(packetActivate);
				PacketSender.SendToAllClients(packetDeactivate);
			}
			else
			{
				PacketSender.SendToHost(packetActivate);
				PacketSender.SendToHost(packetDeactivate);
			}
		}
	}

	/// <summary>
	/// Force ActiveRangeSideScreen to always refresh from component values when SetTarget is called.
	/// This fixes threshold display sync for SmartReservoir, MassageTable, and other IActivationRangeTarget buildings.
	/// </summary>
	[HarmonyPatch(typeof(ActiveRangeSideScreen), nameof(ActiveRangeSideScreen.SetTarget))]
	public static class ActiveRangeSideScreen_SetTarget_Patch
	{
		public static void Postfix(ActiveRangeSideScreen __instance, GameObject new_target)
		{
			using var _ = Profiler.Scope();

			DebugConsole.Log($"[ActiveRangeSideScreen_SetTarget] Called for {new_target?.name ?? "null"}");

			if (__instance.target == null)
			{
				DebugConsole.Log("[ActiveRangeSideScreen_SetTarget] Target is null, returning");
				return;
			}

			// Force update sliders and labels from current component values
			float activateVal = __instance.target.ActivateValue;
			float deactivateVal = __instance.target.DeactivateValue;

			DebugConsole.Log($"[ActiveRangeSideScreen_SetTarget] Reading values: activate={activateVal}, deactivate={deactivateVal}");

			__instance.activateValueSlider.value = activateVal;
			__instance.deactivateValueSlider.value = deactivateVal;
			__instance.activateValueLabel.SetDisplayValue(activateVal.ToString());
			__instance.deactivateValueLabel.SetDisplayValue(deactivateVal.ToString());
			__instance.RefreshTooltips();

			DebugConsole.Log("[ActiveRangeSideScreen_SetTarget] Updated sliders and labels");
		}
	}

	/// <summary>
	/// Sync MassageTable threshold via OnCopySettings (implements IActivationRangeTarget)
	/// </summary>
	[HarmonyPatch(typeof(MassageTable), nameof(MassageTable.OnCopySettings))]
	public static class MassageTable_OnCopySettings_Patch
	{
		public static void Postfix(MassageTable __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			// Send activate value
			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "MassageTableActivate".GetHashCode(),
				Value = __instance.ActivateValue,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			// Send deactivate value
			var packet2 = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "MassageTableDeactivate".GetHashCode(),
				Value = __instance.DeactivateValue,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet2);
			else PacketSender.SendToHost(packet2);
		}
	}

	/// <summary>
	/// Sync MassageTable ActivateValue property changes (threshold slider)
	/// </summary>
	[HarmonyPatch(typeof(MassageTable), "ActivateValue", MethodType.Setter)]
	public static class MassageTable_ActivateValue_Patch
	{
		public static void Postfix(MassageTable __instance, float value)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "MassageTableActivate".GetHashCode(),
				Value = value,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	/// <summary>
	/// Sync MassageTable DeactivateValue property changes (threshold slider)
	/// </summary>
	[HarmonyPatch(typeof(MassageTable), "DeactivateValue", MethodType.Setter)]
	public static class MassageTable_DeactivateValue_Patch
	{
		public static void Postfix(MassageTable __instance, float value)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "MassageTableDeactivate".GetHashCode(),
				Value = value,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	#region BatterySmart (Smart Battery) Patches

	/// <summary>
	/// Sync BatterySmart ActivateValue property changes (upper threshold slider)
	/// </summary>
	[HarmonyPatch(typeof(BatterySmart), "ActivateValue", MethodType.Setter)]
	public static class BatterySmart_ActivateValue_Patch
	{
		public static void Postfix(BatterySmart __instance, float value)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "Activate".GetHashCode(),
				Value = value,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	/// <summary>
	/// Sync BatterySmart DeactivateValue property changes (lower threshold slider)
	/// </summary>
	[HarmonyPatch(typeof(BatterySmart), "DeactivateValue", MethodType.Setter)]
	public static class BatterySmart_DeactivateValue_Patch
	{
		public static void Postfix(BatterySmart __instance, float value)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "Deactivate".GetHashCode(),
				Value = value,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	#endregion

	#region SmartReservoir Direct Property Patches

	/// <summary>
	/// Sync SmartReservoir ActivateValue property changes (threshold slider)
	/// </summary>
	[HarmonyPatch(typeof(SmartReservoir), "ActivateValue", MethodType.Setter)]
	public static class SmartReservoir_ActivateValue_Patch
	{
		public static void Postfix(SmartReservoir __instance, float value)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "SmartReservoirActivate".GetHashCode(),
				Value = value,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	/// <summary>
	/// Sync SmartReservoir DeactivateValue property changes (threshold slider)
	/// </summary>
	[HarmonyPatch(typeof(SmartReservoir), "DeactivateValue", MethodType.Setter)]
	public static class SmartReservoir_DeactivateValue_Patch
	{
		public static void Postfix(SmartReservoir __instance, float value)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "SmartReservoirDeactivate".GetHashCode(),
				Value = value,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	#endregion
}
