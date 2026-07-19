using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Patches.World.SideScreen;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
    [HarmonyPatch(typeof(Toggleable), nameof(Toggleable.Toggle))]
    public static class QueueToggleable
    {
        static void Postfix(Toggleable __instance, int targetIdx)
        {
            using var _ = Profiler.Scope();

            if (!MultiplayerSession.InSession) return;

            bool expectedQueue = __instance.IsToggleQueued(targetIdx);
            SideScreenSyncHelper.SyncQueueToggleable(__instance.gameObject, targetIdx, expectedQueue);
        }
    }

    [HarmonyPatch(typeof(Toggleable), "OnCompleteWork")]
    public static class ToggleableCompleteWorkPatch
    {
		static void Prefix(Toggleable __instance, out int __state, WorkerBase worker)
        {
            using var _ = Profiler.Scope();

            // Get the toggle handler for the completed work.
			__state = __instance.GetTargetForWorker(worker);

            return;
        }

		static void Postfix(Toggleable __instance, int __state)
        {
            using var _ = Profiler.Scope();

            if (!MultiplayerSession.InSession) return;

			if (__state >= 0 && __state < __instance.targets.Count)
			{
				IToggleHandler handler = __instance.targets[__state].Key;
				if (handler == null) return;
				bool isOn = handler.IsHandlerOn();
				DebugConsole.Log($"[ToggleablePatch] Toggleable for {__instance.gameObject.name} Changed");
				SideScreenSyncHelper.SyncToggleableState(__instance.gameObject, __state, isOn);
            }
        }
    }
}
