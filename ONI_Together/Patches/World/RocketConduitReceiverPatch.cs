using HarmonyLib;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
    [HarmonyPatch(typeof(RocketConduitReceiver), nameof(RocketConduitReceiver.FindPartner))]
    internal static class RocketConduitReceiverPatch
    {
        public static bool Prefix(RocketConduitReceiver __instance)
        {
            using var _ = Profiler.Scope();

            if (__instance.senderConduitStorage != null)
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
