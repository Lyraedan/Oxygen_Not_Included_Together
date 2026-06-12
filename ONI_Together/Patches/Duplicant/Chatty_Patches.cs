using HarmonyLib;
using ONI_Together.Networking;
using Shared.Profiling;

namespace ONI_Together.Patches.Duplicant
{
	internal class Chatty_Patches
	{
		[HarmonyPatch(typeof(Chatty), nameof(Chatty.SimEveryTick))]
		public static class Chatty_SimEveryTick_Patch
		{
			public static bool Prefix()
			{
				using var _ = Profiler.Scope();

				return !MultiplayerSession.IsClient;
			}
		}
	}
}
