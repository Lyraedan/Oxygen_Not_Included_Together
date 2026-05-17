using HarmonyLib;
using ONI_Together.UI;
using Shared.Profiling;

[HarmonyPatch(typeof(KInputManager), nameof(KInputManager.Update))]
public static class KInputManagerPatch
{
	static bool Prefix()
	{
		using var _ = Profiler.Scope();

		// Suppress input processing while typing in chat
		if (ChatScreen.IsFocused())
		{
			return false; // Skip Update() entirely
		}

		return true; // Allow input through
	}
}
