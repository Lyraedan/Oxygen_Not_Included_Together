using HarmonyLib;
using ONI_Together.Networking;
using Shared.Profiling;

namespace ONI_Together.Patches.Duplicant
{
	internal class DupeGreetingManager_Patches
	{
		[HarmonyPatch(typeof(DupeGreetingManager), nameof(DupeGreetingManager.Sim200ms))]
		public static class DupeGreetingManager_Sim200ms_Patch
		{
			public static bool Prefix()
			{
				using var _ = Profiler.Scope();

				return !MultiplayerSession.IsClient;
			}
		}
	}
}
