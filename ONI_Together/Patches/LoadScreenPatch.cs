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

	[HarmonyPatch(typeof(LoadScreen), "MigrateFile")]
	public static class MigrateFilePatch
	{
		[HarmonyPrefix]
		public static bool Prefix(string source, string dest, bool ignoreMissing)
		{
			try
			{
				if (!System.IO.File.Exists(source))
				{
					return false;
				}

				string destDir = System.IO.Path.GetDirectoryName(dest);
				if (!string.IsNullOrEmpty(destDir) && !System.IO.Directory.Exists(destDir))
				{
					System.IO.Directory.CreateDirectory(destDir);
				}
			}
			catch
			{
				return false;
			}
			return true;
		}

		[HarmonyFinalizer]
		public static System.Exception Finalizer(System.Exception __exception)
		{
			if (__exception != null)
			{
				DebugConsole.LogWarning($"[LoadScreenPatch] Suppressed MigrateFile error: {__exception.Message}");
			}
			return null;
		}
	}

	[HarmonyPatch(typeof(LoadScreen), "CheckCloudLocalOverlap")]
	public static class CheckCloudLocalOverlapPatch
	{
		[HarmonyFinalizer]
		public static System.Exception Finalizer(System.Exception __exception)
		{
			if (__exception != null)
			{
				DebugConsole.LogWarning($"[LoadScreenPatch] Suppressed CheckCloudLocalOverlap error: {__exception.Message}");
			}
			return null;
		}
	}
}

