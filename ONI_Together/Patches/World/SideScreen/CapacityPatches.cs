using HarmonyLib;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World.SideScreen
{
	/// <summary>
	/// Patches for capacity control side screens (batteries, gas reservoirs, etc.)
	/// </summary>

	/// <summary>
	/// Sync capacity changes from CapacityControlSideScreen
	/// </summary>
	[HarmonyPatch(typeof(CapacityControlSideScreen), nameof(CapacityControlSideScreen.UpdateMaxCapacity))]
	public static class CapacityControlSideScreen_UpdateMaxCapacity_Patch
	{
		public static void Postfix(CapacityControlSideScreen __instance, float newValue)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (__instance.target == null) return;

			// Get the GameObject from the IUserControlledCapacity target
			var targetComponent = __instance.target as Component;
			if (targetComponent != null)
			{
				SideScreenSyncHelper.SyncCapacityChange(targetComponent.gameObject, newValue);
			}
		}
	}

	/// <summary>
	/// Register NetworkIdentity when capacity side screen is opened
	/// </summary>
	[HarmonyPatch(typeof(CapacityControlSideScreen), nameof(CapacityControlSideScreen.SetTarget))]
	public static class CapacityControlSideScreen_SetTarget_Patch
	{
		public static void Postfix(CapacityControlSideScreen __instance, GameObject new_target)
		{
			using var _ = Profiler.Scope();

			if (new_target == null) return;
			var identity = new_target.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();
		}
	}
}
