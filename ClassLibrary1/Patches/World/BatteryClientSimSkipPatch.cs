using HarmonyLib;
using ONI_MP.Networking;
using Shared.Profiling;

namespace ONI_MP.Patches.World
{
	// Clients receive joules snapshots from the host via StructureStateSyncer.
	// If the local power sim also runs, it overwrites those snapshots every 200ms,
	// so batteries drift and never appear to charge/discharge correctly.
	internal static class BatteryClientSimSkipPatch
	{
		private static bool SkipOnClient()
		{
			using var _ = Profiler.Scope();
			return MultiplayerSession.IsClient;
		}

		[HarmonyPatch(typeof(Battery), nameof(Battery.EnergySim200ms))]
		public static class Battery_EnergySim200ms_Patch
		{
			public static bool Prefix() => !SkipOnClient();
		}

		[HarmonyPatch(typeof(Battery), nameof(Battery.AddEnergy))]
		public static class Battery_AddEnergy_Patch
		{
			public static bool Prefix() => !SkipOnClient();
		}

		[HarmonyPatch(typeof(Battery), nameof(Battery.ConsumeEnergy), new[] { typeof(float) })]
		public static class Battery_ConsumeEnergy_Patch
		{
			public static bool Prefix() => !SkipOnClient();
		}
	}
}
