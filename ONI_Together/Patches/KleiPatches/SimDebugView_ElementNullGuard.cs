using HarmonyLib;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.KleiPatches
{
	/// <summary>
	/// Guards the SimDebugView overlay colour methods against a null
	/// <c>Grid.Element[cell]</c> reference.
	///
	/// The simulation can transiently leave a cell's resolved element reference
	/// null (element being destroyed/replaced mid-tick). The overlay's
	/// per-cell colour methods dereference <c>Grid.Element[cell].id</c> directly
	/// on the job threads (SimDebugView.UpdateData -> WorkItemCollection ->
	/// JobManager), so a null there throws NullReferenceException and crashes the
	/// client. Multiplayer makes this observable far more often because element
	/// state is synced to the client and rewritten frequently.
	///
	/// Each affected method is static and shares the same signature
	/// <c>static Color(SimDebugView instance, int cell)</c>. The prefix skips the
	/// original body (returning a neutral black, i.e. "no overlay data") when the
	/// cell is invalid or its element reference is null, instead of letting the
	/// dereference crash the job thread.
	/// </summary>
	internal static class SimDebugView_ElementNullGuard
	{
		// Broad guard shared by all patched methods. Returns true when it is safe
		// to run the original body (cell valid AND element resolved).
		private static bool HasResolvedElement(int cell)
		{
			return Grid.IsValidCell(cell) && Grid.Element[cell] != null;
		}

		[HarmonyPatch(typeof(SimDebugView), nameof(SimDebugView.GetOxygenMapColour))]
		private static class GetOxygenMapColour_Patch
		{
			private static bool Prefix(SimDebugView instance, int cell, ref Color __result)
			{
				if (HasResolvedElement(cell))
					return true;
				__result = Color.black;
				return false;
			}
		}

		[HarmonyPatch(typeof(SimDebugView), nameof(SimDebugView.GetStateMapColour))]
		private static class GetStateMapColour_Patch
		{
			private static bool Prefix(SimDebugView instance, int cell, ref Color __result)
			{
				if (HasResolvedElement(cell))
					return true;
				__result = Color.black;
				return false;
			}
		}

		[HarmonyPatch(typeof(SimDebugView), nameof(SimDebugView.GetSolidLiquidMapColour))]
		private static class GetSolidLiquidMapColour_Patch
		{
			private static bool Prefix(SimDebugView instance, int cell, ref Color __result)
			{
				if (HasResolvedElement(cell))
					return true;
				__result = Color.black;
				return false;
			}
		}

		[HarmonyPatch(typeof(SimDebugView), nameof(SimDebugView.GetSimCheckErrorMapColour))]
		private static class GetSimCheckErrorMapColour_Patch
		{
			private static bool Prefix(SimDebugView instance, int cell, ref Color __result)
			{
				if (HasResolvedElement(cell))
					return true;
				__result = Color.black;
				return false;
			}
		}
	}
}
