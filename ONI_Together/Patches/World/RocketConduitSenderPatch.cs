using HarmonyLib;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
    [HarmonyPatch(typeof(RocketConduitSender), nameof(RocketConduitSender.FindPartner))]
    internal static class RocketConduitSenderPatch
    {
        public static bool Prefix(RocketConduitSender __instance)
        {
            using var _ = Profiler.Scope();

            if (__instance.partnerReceiver != null)
                return true;

            WorldContainer world = ClusterManager.Instance.GetWorld(__instance.gameObject.GetMyWorldId());
            if (world == null)
                return false;

            if (world.IsModuleInterior)
            {
                var clustercraft = world.GetComponent<Clustercraft>();
                if (clustercraft?.ModuleInterface?.GetPassengerModule() == null)
                    return false;
            }
            else if (__instance.GetComponent<ClustercraftExteriorDoor>() == null)
            {
                return false;
            }

            return true;
        }
    }
}
