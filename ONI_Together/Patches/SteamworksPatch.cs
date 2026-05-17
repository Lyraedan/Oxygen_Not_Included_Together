using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Transport.Steamworks;
using Shared.Profiling;

namespace ONI_Together.Patches
{
	[HarmonyPatch]
	public static class SteamworksPatch
	{
		// Patch SteamManager.Awake to hook after Steam is initialized
		[HarmonyPatch(typeof(SteamManager), "Awake")]
		[HarmonyPostfix]
		public static void OnSteamAwake()
		{
			using var _ = Profiler.Scope();

			if (SteamManager.Initialized)
			{
				DebugConsole.Log("Steam initialized – setting rich presence.");
				SteamRichPresence.SetStatus("Multiplayer – In Main Menu");
			}
			else
			{
				DebugConsole.LogWarning("Steam not initialized – skipping rich presence setup.");
			}
		}

		// Optionally clear rich presence when SteamManager is destroyed
		[HarmonyPatch(typeof(SteamManager), "OnDestroy")]
		[HarmonyPrefix]
		public static void OnSteamShutdown()
		{
			using var _ = Profiler.Scope();

			if (SteamManager.Initialized)
			{
				DebugConsole.Log("SteamManager shutting down – clearing rich presence.");
				SteamRichPresence.Clear();
			}
		}
	}
}

