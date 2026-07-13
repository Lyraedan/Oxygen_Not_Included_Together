using HarmonyLib;
using ONI_Together.Menus;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.UI;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.GamePatches
{
	[HarmonyPatch(typeof(GameScreenManager), nameof(GameScreenManager.OnSpawn))]
	public static class GameScreenPatch
	{
		static void Postfix(GameScreenManager __instance)
		{
			using var _ = Profiler.Scope();

			// Setup indicators
			NetworkIndicatorsScreen.Show();

			// Setup chat window
            //ChatScreen.Show();
            UnityChatBoxUI.InitScreen();
            UnityChatBoxUI.Instance.gameObject.AddComponent<OxySyncChat>();
		}
	}

}
