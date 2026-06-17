using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World
{
    [HarmonyPatch(typeof(CreatureSimTemperatureTransfer), nameof(CreatureSimTemperatureTransfer.Update))]
    internal static class CreatureTemperaturePatch
    {
        private static readonly FieldInfo elementChunksField =
            typeof(SimData).GetField("elementChunks", BindingFlags.Public | BindingFlags.Instance);

        public static bool Prefix(CreatureSimTemperatureTransfer __instance)
        {
            using var _ = Profiler.Scope();

            if (MultiplayerSession.IsClient) return false;
            
            if (Game.Instance?.simData == null) return false;
            if (__instance.average_kilowatts_exchanged == null) return false;

            try
            {
                if (elementChunksField?.GetValue(Game.Instance.simData) == null)
                    return false;
            }
            catch
            {
                return false;
            }

            __instance.unsafeUpdateAverageKiloWattsExchanged(Time.deltaTime);
            return false;
        }
    }
}
