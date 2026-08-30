using HarmonyLib;
using ONI_Together.Networking.Components;
using Shared.Profiling;

namespace ONI_Together.Patches.StateMachines
{
	internal class StateMachineSyncPatches
	{
		[HarmonyPatch(typeof(StateMachineController), nameof(StateMachineController.StartSMIS))]
		public static class StateMachineController_StartSMIS_Patch
		{
			public static void Postfix(StateMachineController __instance)
			{
				using var _ = Profiler.Scope();

				if (__instance == null)
					return;

				foreach (var smi in __instance.GetAllSMI<StateMachine.Instance>())
				{
					if (!StateMachineStateSyncer.IsSupported(smi))
						continue;

					__instance.gameObject.AddOrGet<NetworkIdentity>();
					__instance.gameObject.AddOrGet<StateMachineStateSyncer>();
					break;
				}
			}
		}
	}
}
