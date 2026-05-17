using HarmonyLib;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World.SideScreen
{
	/// <summary>
	/// Patches for threshold side screens (temperature, pressure, gas sensors, etc.)
	/// </summary>

	/// <summary>
	/// Sync threshold value changes (e.g., temperature/pressure sensors)
	/// </summary>
	[HarmonyPatch(typeof(ThresholdSwitchSideScreen), nameof(ThresholdSwitchSideScreen.UpdateThresholdValue))]
	public static class ThresholdSwitchSideScreen_UpdateThresholdValue_Patch
	{
		public static void Postfix(ThresholdSwitchSideScreen __instance, float newValue)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (__instance.target == null) return;

			SideScreenSyncHelper.SyncThresholdChange(__instance.target, newValue);
		}
	}

	/// <summary>
	/// Sync threshold direction changes (above/below)
	/// </summary>
	[HarmonyPatch(typeof(ThresholdSwitchSideScreen), nameof(ThresholdSwitchSideScreen.OnConditionButtonClicked))]
	public static class ThresholdSwitchSideScreen_OnConditionButtonClicked_Patch
	{
		public static void Postfix(ThresholdSwitchSideScreen __instance, bool activate_above_threshold)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (__instance.target == null) return;

			SideScreenSyncHelper.SyncThresholdDirection(__instance.target, activate_above_threshold);
		}
	}

	/// <summary>
	/// Register NetworkIdentity when the side screen is opened
	/// </summary>
	[HarmonyPatch(typeof(ThresholdSwitchSideScreen), nameof(ThresholdSwitchSideScreen.SetTarget))]
	public static class ThresholdSwitchSideScreen_SetTarget_Patch
	{
		public static void Postfix(ThresholdSwitchSideScreen __instance, GameObject new_target)
		{
			using var _ = Profiler.Scope();

			if (new_target == null) return;
			var identity = new_target.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();
		}
	}
}
