using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.UI
{
	internal class UI_Patches
	{
		public static System.Action OnLoadScreenExited = null;
		[HarmonyPatch(typeof(KScreen), nameof(KScreen.OnKeyDown))]
		public class KScreen_OnKeyDowne_Patch
		{
			public static void Prefix(KScreen __instance, KButtonEvent e)
			{
				using var _ = Profiler.Scope();

				if (__instance is LoadScreen && (e.IsAction(Action.Escape) || e.IsAction(Action.MouseRight)))
				{
					DebugConsole.Log("On LoadScreen deactivate");
					MultiplayerSession.ShouldHostAfterLoad = false;
					if (OnLoadScreenExited != null)
					{
						OnLoadScreenExited();
					}
				}
			}
		}


		[HarmonyPatch(typeof(NewGameFlow), nameof(NewGameFlow.Previous))]
		public class NewGameFlow_Previous_Patch
		{
			public static void Postfix(NewGameFlow __instance)
			{
				using var _ = Profiler.Scope();

				if (__instance.currentScreenIndex < 0)
				{
					MultiplayerSession.ShouldHostAfterLoad = false;
					DebugConsole.Log("On NewGameFlow deactivate");
				}
			}
		}
	}
}
