using HarmonyLib;
using ONI_Together.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.Duplicant
{
	internal class Flatulence_Patch
	{

		[HarmonyPatch(typeof(Flatulence), nameof(Flatulence.Emit))]
		public class Flatulence_Emit_Patch
		{
			/// <summary>
			/// Skip farting for printing pod preview duplicants
			/// </summary>
			/// <param name="data"></param>
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

				// Dupes are somehow still farting at the minion select screen of the printing pod because the sim is running. So if the sim isn't paused, prevent farting
				if (!SpeedControlScreen.Instance.IsPaused)
					return false;

				return true;
			}
		}
	}
}
