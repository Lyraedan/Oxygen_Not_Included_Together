using HarmonyLib;
using ONI_Together.DebugTools;
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
            DebugConsole.Log("Unity chatbox init!");
            var chatHost = new GameObject("OxySyncChatHost");
            chatHost.AddComponent<OxySyncChat>();
            DebugConsole.Log("Unity chatbox added oxysync");
		}
	}

}
