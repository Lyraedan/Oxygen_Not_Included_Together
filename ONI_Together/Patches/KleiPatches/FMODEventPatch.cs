using FMODUnity;
using HarmonyLib;
using ONI_Together.DebugTools;
using System;
using Shared.Profiling;

namespace ONI_Together.Patches.KleiPatches
{
    /// <summary>
    /// Patches FMOD audio event lookups to handle cross-platform compatibility issues.
    /// Mac and Windows clients have different audio bank GUIDs, causing EventNotFoundException
    /// when a Mac client connects to a Windows host (or vice versa).
    /// This patch catches the exception and returns a fallback, preventing crashes.
    /// </summary>
    [HarmonyPatch(
    typeof(Assets),
    nameof(Assets.GetSimpleSoundEventName),
    new Type[] { typeof(FMODUnity.EventReference) }
)]
    public static class FMODEventPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(FMODUnity.EventReference event_ref, ref string __result)
        {
	        using var _ = Profiler.Scope();

            try
            {
                var path = KFMOD.GetEventReferencePath(event_ref);
                if (string.IsNullOrEmpty(path))
                {
                    __result = string.Empty;
                    return false;
                }

                return true; // let original run
            }
            catch (FMODUnity.EventNotFoundException)
            {
                DebugConsole.LogWarning(
                    $"[Multiplayer] FMOD event not found (cross-platform audio GUID mismatch): {event_ref.Guid}"
                );
                __result = string.Empty;
                return false;
            }
            catch (Exception ex)
            {
                DebugConsole.LogWarning(
                    $"[Multiplayer] FMOD event lookup failed: {ex.Message}"
                );
                __result = string.Empty;
                return false;
            }
        }
    }

    /// <summary>
    /// Patches KFMOD.GetEventReferencePath to handle missing events gracefully.
    /// </summary>
    [HarmonyPatch(typeof(KFMOD), nameof(KFMOD.GetEventReferencePath))]
	public static class KFMODEventPathPatch
	{
		[HarmonyPrefix]
		public static bool Prefix(FMODUnity.EventReference event_ref, ref string __result)
		{
			using var _ = Profiler.Scope();

			try
			{
				// Check if the event reference is null or empty
				if (event_ref.IsNull)
				{
					__result = string.Empty;
					return false;
				}

				// Try to get the event description
				var desc = FMODUnity.RuntimeManager.GetEventDescription(event_ref.Guid);
				if (!desc.isValid())
				{
					__result = string.Empty;
					return false;
				}

				// Let original method proceed
				return true;
			}
			catch (FMODUnity.EventNotFoundException)
			{
				// GUID not found on this platform
				__result = string.Empty;
				return false;
			}
			catch (Exception)
			{
				__result = string.Empty;
				return false;
			}
		}
	}

	/// <summary>
	/// Patches MusicManager.ConfigureSongs to wrap the entire method in a try-catch
	/// to prevent crashes during music initialization on cross-platform connections.
	/// </summary>
	[HarmonyPatch(typeof(MusicManager), nameof(MusicManager.ConfigureSongs))]
	public static class MusicManagerPatch
	{
		[HarmonyFinalizer]
		public static Exception Finalizer(Exception __exception)
		{
			using var _ = Profiler.Scope();

			if (__exception is FMODUnity.EventNotFoundException)
			{
				// Suppress the exception - music will be disabled but game will continue
				DebugConsole.LogWarning($"[Multiplayer] Music configuration failed due to cross-platform audio incompatibility. Music may be disabled.");
				return null; // Suppress the exception
			}
			return __exception; // Re-throw other exceptions
		}
	}
}
