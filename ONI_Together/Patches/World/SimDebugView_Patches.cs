using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ONI_Together.Patches.World
{
	/// <summary>
	/// Guards SimDebugView overlay colour callbacks against transiently null Grid.Element[cell]
	/// entries while cells are being replaced on clients (fixes NRE on JobManager worker threads).
	/// </summary>
	[HarmonyPatch]
	internal static class SimDebugView_NullElement_Patch
	{
		private static readonly string[] TargetMethodNames =
		{
			nameof(SimDebugView.GetOxygenMapColour),
			nameof(SimDebugView.GetNormalizedTemperatureColour),
			nameof(SimDebugView.GetThermalConductivityColour),
			nameof(SimDebugView.GetPressureMapColour),
			nameof(SimDebugView.GetTileColour),
			nameof(SimDebugView.GetTileTypeColour),
			nameof(SimDebugView.GetStateMapColour),
			nameof(SimDebugView.GetSolidLiquidMapColour),
			nameof(SimDebugView.GetStateChangeColour),
			nameof(SimDebugView.GetStateChangeProximityColour),
			nameof(SimDebugView.GetMassColour),
		};

		static IEnumerable<MethodBase> TargetMethods()
		{
			var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
			foreach (var name in TargetMethodNames)
			{
				var method = typeof(SimDebugView).GetMethod(name, flags, null, new[] { typeof(SimDebugView), typeof(int) }, null);
				if (method != null)
					yield return method;
			}
		}

		static bool Prefix(int cell, ref Color __result)
		{
			var elements = Grid.Element;
			if (elements == null || (uint)cell >= (uint)elements.Length || elements[cell] == null)
			{
				__result = Color.black;
				return false;
			}
			return true;
		}
	}
}
