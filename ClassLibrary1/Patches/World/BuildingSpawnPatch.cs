using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking.Components;
using Shared.Profiling;

namespace ONI_MP.Patches.World
{
	// Adds NetworkIdentity to buildings that need it for BuildingConfigPacket or other interactions
	// Adds NetworkIdentity to buildings that need it
	[HarmonyPatch(typeof(Building), "OnSpawn")]
	public static class BuildingSpawnPatch
	{
		public static void Postfix(Building __instance)
		{
			using var _ = Profiler.Scope();
			try
			{
				PostfixBody(__instance);
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[BuildingSpawnPatch] {ex}");
			}
		}

		private static void PostfixBody(Building __instance)
		{
			var go = __instance.gameObject;

			// We skip construction for configuration sync usually,
			// but having an ID early doesn't hurt.
			// Let's focus on BuildingComplete for settings sync.
			if (!(__instance is BuildingComplete)) return;

			bool isAnimatedBuildingCandidate = AnimSyncEligibility.IsAnimatedBuilding(go);
			bool needsIdentity = false;

			// Check for components that require NetID
			if (go.GetComponent<LogicSwitch>() != null) needsIdentity = true;
			else if (go.GetComponent<Valve>() != null) needsIdentity = true;
			else if (go.GetComponent<IThresholdSwitch>() != null) needsIdentity = true;
			else if (go.GetComponent<IActivationRangeTarget>() != null) needsIdentity = true;
			else if (go.GetComponent<ISliderControl>() != null) needsIdentity = true;
			else if (go.GetComponent<ISingleSliderControl>() != null) needsIdentity = true;
			else if (go.GetComponent<ICheckboxControl>() != null) needsIdentity = true;
			else if (go.GetComponent<IUserControlledCapacity>() != null) needsIdentity = true;
			else if (go.GetComponent<ISidescreenButtonControl>() != null) needsIdentity = true;
			else if (go.GetComponent<Door>() != null) needsIdentity = true;
			else if (go.GetComponent<LimitValve>() != null) needsIdentity = true;
			else if (go.GetComponent<Compost>() != null) needsIdentity = true;
			else if (go.GetComponent<StorageLocker>() != null) needsIdentity = true;
			else if (go.GetComponent<Refrigerator>() != null) needsIdentity = true;
			else if (go.GetComponent<RationBox>() != null) needsIdentity = true;
			else if (isAnimatedBuildingCandidate) needsIdentity = true;

			if (needsIdentity)
			{
				var identity = go.AddOrGet<NetworkIdentity>();
				// We call RegisterIdentity explicitly to ensure it happens
				// even if the component was already there but not registered.
				identity.RegisterIdentity();
			}
		}
	}
}
