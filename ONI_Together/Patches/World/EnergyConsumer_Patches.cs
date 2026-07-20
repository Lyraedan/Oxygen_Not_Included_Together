using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Scripts.Buildings;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
    internal static class EnergyConsumer_Patches
    {
        private static bool SkipOnClient()
        {
            using var _ = Profiler.Scope();
            return MultiplayerSession.IsClient;
        }

        [HarmonyPatch(typeof(EnergyConsumer), nameof(EnergyConsumer.SetConnectionStatus))]
        public static class EnergyConsumer_SetConnectionStatus_Patch
        {
            public static bool Prefix() => !SkipOnClient();
        }

        [HarmonyPatch(typeof(EnergyConsumerSelfSustaining), nameof(EnergyConsumerSelfSustaining.SetConnectionStatus))]
        public static class EnergyConsumerSelfSustaining_SetConnectionStatus_Patch
        {
            public static bool Prefix() => !SkipOnClient();
        }

        [HarmonyPatch(typeof(EnergyConsumerSelfSustaining), nameof(EnergyConsumerSelfSustaining.IsPowered), MethodType.Getter)]
        public static class EnergyConsumerSelfSustaining_IsPowered_Getter_Patch
        {
            public static bool Prefix(EnergyConsumerSelfSustaining __instance, ref bool __result)
            {
                if (!SkipOnClient())
                    return true;

                if (__instance.TryGetComponent<ClientReceiver_Operational>(out var wrap))
                {
                    __result = wrap.IsPowered;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Operational), nameof(Operational.GetFlag))]
        public static class Operational_GetFlag_Patch
        {
            public static bool Prefix(Operational __instance, Operational.Flag flag, ref bool __result)
            {
                if (!SkipOnClient())
                    return true;

                if (flag != EnergyConsumer.PoweredFlag)
                    return true;

                if (__instance.TryGetComponent<ClientReceiver_Operational>(out var wrap))
                {
                    __result = wrap.IsPowered;
                    return false;
                }
                return true;
            }
        }
    }
}
