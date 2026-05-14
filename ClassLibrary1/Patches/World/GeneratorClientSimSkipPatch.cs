using HarmonyLib;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.World;
using Shared.Profiling;
using static EnergyGenerator;
using static STRINGS.BUILDINGS.PREFABS;

namespace ONI_MP.Patches.World
{
    internal static class GeneratorClientSimSkipPatch
    {
        private static bool SkipOnClient()
        {
            using var _ = Profiler.Scope();
            return MultiplayerSession.IsClient;
        }

        [HarmonyPatch(typeof(EnergyGenerator), nameof(EnergyGenerator.EnergySim200ms), typeof(float))]
        public static class EnergyGenerator_EnergySim200ms_Patch
        {
            public static bool Prefix(EnergyGenerator __instance, float dt)
            {
                return !SkipOnClient();
            }
        }

        [HarmonyPatch(typeof(Generator), nameof(Generator.ConsumeEnergy), typeof(float))]
        public static class Generator_ConsumeEnergy_Patch
        {
            public static bool Prefix(Generator __instance, float joules)
            {
                return !SkipOnClient();
            }
        }
    }
}
