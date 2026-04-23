using HarmonyLib;
using ONI_MP.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Patches.Duplicant
{
	internal class Flatulence_Patch
	{

		[HarmonyPatch(typeof(Flatulence), nameof(Flatulence.Emit))]
		public class Flatulence_Emit_Patch
		{
			/// <summary>
			/// Skip farting for printing pod preview duplicants
			/// </summary>
			/// <param name="__instance"></param>
			/// <returns></returns>
			public static bool Prefix(Flatulence __instance)
			{
				using var _ = Profiler.Scope();

				if(__instance.IsNullOrDestroyed() || __instance.gameObject.IsNullOrDestroyed())
					return false;

				if (__instance.smi.IsNullOrDestroyed() || __instance.smi.IsNullOrStopped())
					return false;

				bool preview = (__instance.PrefabID() != GameTags.MinionSelectPreview);
				bool client = MultiplayerSession.IsClient;

				if (client || preview)
					return false;

				return true;
			}
		}
	}
}
