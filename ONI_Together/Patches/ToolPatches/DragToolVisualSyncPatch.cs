using HarmonyLib;
using ONI_Together.Networking.Components;
using Shared.Profiling;
using System.Collections.Generic;
using System.Reflection;

namespace ONI_Together.Patches.ToolPatches
{
	[HarmonyPatch]
	public static class DragToolVisualSyncPatch
	{
		public static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(DragTool), nameof(DragTool.OnLeftClickDown));
			yield return AccessTools.Method(typeof(BrushTool), nameof(BrushTool.OnLeftClickDown));
		}

		[HarmonyPostfix]
		public static void SendDragStartImmediately()
		{
			using var _ = Profiler.Scope();

			CursorManager.Instance?.SendCursorPositionNow();
		}
	}

	[HarmonyPatch]
	public static class DragToolFinalVisualSyncPatch
	{
		public static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(DragTool), nameof(DragTool.OnLeftClickUp));
			yield return AccessTools.Method(typeof(BrushTool), nameof(BrushTool.OnLeftClickUp));
		}

		[HarmonyPrefix]
		public static void SendFinalDragExtentImmediately()
		{
			using var _ = Profiler.Scope();

			// Send while Dragging is still true so short drags cannot lose their
			// final endpoint between two periodic cursor updates.
			CursorManager.Instance?.SendCursorPositionNow();
		}
	}
}
