using HarmonyLib;
using ONI_MP.UI;
using Shared.Profiling;
using ONI_MP.Menus;

namespace ONI_MP.Patches
{
[HarmonyPatch(typeof(CameraController), nameof(CameraController.OnKeyDown))]
public static class CameraControllerPatch
{
	static bool Prefix(KButtonEvent e)
	{
		using var _ = Profiler.Scope();

		// Block camera zoom if mouse is over chat panel
		if (ChatScreen.IsMouseOverChatPanel())
		{
			if (e.IsAction(Action.ZoomIn) || e.IsAction(Action.ZoomOut))
			{
				// Skip the original method
				return false;
			}
		}

		// Allow original method to run
		return true;
	}
}

}