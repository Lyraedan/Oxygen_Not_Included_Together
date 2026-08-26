using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using System.Reflection;
using Shared.Profiling;

namespace ONI_Together.Patches
{
	[HarmonyPatch]
	public static class DoLoadPatch
	{
		// Explicitly resolve the exact DoLoad(string) method
		[HarmonyTargetMethod]
		public static MethodBase TargetMethod()
		{
			using var _ = Profiler.Scope();

			return typeof(LoadScreen).GetMethod(
					"DoLoad",
					BindingFlags.Static | BindingFlags.Public,
					null,
					new[] { typeof(string) },
					null
			);
		}

		// Updating this bool here doesn't affect SP
		[HarmonyPrefix]
		public static void Prefix_DoLoad(string filename)
		{
			using var _ = Profiler.Scope();

			DebugConsole.Log($"Loading {filename}");

			// A live server still references the scene that LoadScreen is about to
			// destroy. Stop it first and recreate it after the save has loaded.
			if (MultiplayerSession.IsHostInSession)
			{
				MultiplayerSession.ShouldHostAfterLoad = true;
				NetworkConfig.Stop();
			}
		}

		[HarmonyPostfix]
		public static void Postfix_DoLoad(string filename)
		{
			using var _ = Profiler.Scope();

			DebugConsole.Log($"Loaded {filename}");
		}
	}
}
