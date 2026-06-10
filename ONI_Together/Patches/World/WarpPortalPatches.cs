using HarmonyLib;

namespace ONI_Together.Patches.World;

public class WarpPortalPatches
{
    [HarmonyPatch(typeof(WarpPortal.WarpPortalSM.Instance), nameof(WarpPortal.WarpPortalSM.Instance.CreateDupeWaitingNotification))]
    public static class WarpPortalPatch
    {
        [HarmonyPostfix]
        public static void Postfix(WarpPortal.WarpPortalSM.Instance __instance, ref Notification __result)
        {
            if (__result != null)
            {
                string dupeName = global::STRINGS.MISC.NOTIFICATIONS.WARP_PORTAL_DUPE_READY.NAME;
                WorkerBase worker = __instance.master.worker;
                if (worker != null)
                {
                    dupeName = worker.name;
                }
                __result.ToolTip = (_, _) =>  global::STRINGS.MISC.NOTIFICATIONS.WARP_PORTAL_DUPE_READY.TOOLTIP.Replace("{dupe}", dupeName);
            }
        }
    }
}