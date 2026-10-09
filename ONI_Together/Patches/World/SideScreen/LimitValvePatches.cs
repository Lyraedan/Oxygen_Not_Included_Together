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
	/// Patches for LimitValve (Meter Valve) synchronization
	/// </summary>

	[HarmonyPatch(typeof(LimitValve), nameof(LimitValve.OnCopySettings))]
	public static class LimitValve_OnCopySettings_Patch
	{
		public static void Postfix(LimitValve __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InActiveSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "LimitValve".GetHashCode(),
				Value = __instance.Limit,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	[HarmonyPatch(typeof(LimitValveSideScreen), nameof(LimitValveSideScreen.OnReleaseHandle))]
	public static class LimitValveSideScreen_OnReleaseHandle_Patch
	{
		public static void Postfix(LimitValveSideScreen __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InActiveSession) return;
			if (__instance.targetLimitValve == null) return;

			var identity = __instance.targetLimitValve.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.targetLimitValve.gameObject),
				ConfigHash = "LimitValve".GetHashCode(),
				Value = __instance.targetLimit,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[LimitValveSideScreen_OnReleaseHandle] Synced Limit={__instance.targetLimit}");
		}
	}

	[HarmonyPatch(typeof(LimitValveSideScreen), nameof(LimitValveSideScreen.ReceiveValueFromInput))]
	public static class LimitValveSideScreen_ReceiveValueFromInput_Patch
	{
		public static void Postfix(LimitValveSideScreen __instance, float input)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InActiveSession) return;
			if (__instance.targetLimitValve == null) return;

			var identity = __instance.targetLimitValve.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.targetLimitValve.gameObject),
				ConfigHash = "LimitValve".GetHashCode(),
				Value = input,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[LimitValveSideScreen_ReceiveValueFromInput] Synced Limit={input}");
		}
	}

	[HarmonyPatch(typeof(LimitValveSideScreen), nameof(LimitValveSideScreen.SetTarget))]
	public static class LimitValveSideScreen_SetTarget_Patch
	{
		public static void Postfix(LimitValveSideScreen __instance, GameObject target)
		{
			using var _ = Profiler.Scope();

			if (__instance.targetLimitValve == null) return;

			float currentLimit = __instance.targetLimitValve.Limit;
			__instance.limitSlider.value = __instance.limitSlider.GetPercentageFromValue(currentLimit);
			__instance.targetLimit = currentLimit;

			if (__instance.targetLimitValve.displayUnitsInsteadOfMass)
			{
				__instance.numberInput.SetDisplayValue(GameUtil.GetFormattedUnits(
					Mathf.Max(0f, currentLimit),
					GameUtil.TimeSlice.None,
					displaySuffix: false,
					LimitValveSideScreen.FLOAT_FORMAT));
			}
			else
			{
				__instance.numberInput.SetDisplayValue(GameUtil.GetFormattedMass(
					Mathf.Max(0f, currentLimit),
					GameUtil.TimeSlice.None,
					GameUtil.MetricMassFormat.Kilogram,
					includeSuffix: false,
					LimitValveSideScreen.FLOAT_FORMAT));
			}
		}
	}
}
