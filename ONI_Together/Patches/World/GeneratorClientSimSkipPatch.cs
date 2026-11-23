using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;
using static EnergyGenerator;
using static STRINGS.BUILDINGS.PREFABS;

namespace ONI_Together.Patches.World
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
                if (!SkipOnClient()) return true;

                // Client: run only essential visual/flag updates.
                // Skip fuel consumption, power generation, and waste emission
                // (those are synced from host via EnergyGeneratorSyncer).

                __instance.CheckConnectionStatus();

                // GeneratorFuelSyncer owns the client meter. Reading the unsimulated
                // local storage here overwrote the authoritative host value with zero.

                ushort circuitID = __instance.CircuitID;
                __instance.operational.SetFlag(Generator.wireConnectedFlag, circuitID != ushort.MaxValue);

                return false;
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
