using ONI_Together.DebugTools;
using ONI_Together.UI;
using ONI_Together.UI.lib;
using Shared.Profiling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UI.lib;
using UnityEngine;

namespace ONI_Together
{
	public static class ModAssets
	{

		public static GameObject ParentScreen => App.GetCurrentSceneName() == "frontend" ? FrontEndManager.Instance.gameObject : PauseScreen.Instance?.transform?.parent?.gameObject ?? GameScreenManager.Instance.GetParent(GameScreenManager.UIRenderTarget.ScreenSpaceOverlay);
		public static GameObject MP_ScreenPrefab, MP_PW_Dialogue, MP_LobbyState_Dialogue, MP_Chatbox;

		public static void LoadAssetBundles()
		{
			using var _ = Profiler.Scope();

			var bundle = AssetUtils.LoadAssetBundle("oni_mp_ui_assets", platformSpecific: true);
			MP_ScreenPrefab = bundle.LoadAsset<GameObject>("Assets/UIs/mp_screen.prefab");
			MP_PW_Dialogue = bundle.LoadAsset<GameObject>("Assets/UIs/mp_password_dialogue.prefab");
			MP_LobbyState_Dialogue = bundle.LoadAsset<GameObject>("Assets/UIs/mp_lobby_state_dialogue.prefab");
			MP_Chatbox = bundle.LoadAsset<GameObject>("Assets/UIs/mp_chatbox.prefab");
			MP_Chatbox.AddComponent<UnityChatBoxUI>();

			var TMPConverter = new TMPConverter();
			DebugConsole.Log("Loading main screen prefab...");
			TMPConverter.ReplaceAllText(MP_ScreenPrefab);
			DebugConsole.Log("Loading password dialogue prefab...");
			TMPConverter.ReplaceAllText(MP_PW_Dialogue);
			DebugConsole.Log("Loading lobby state dialogue prefab...");
			TMPConverter.ReplaceAllText(MP_LobbyState_Dialogue);
			DebugConsole.Log("Loading chatbox prefab...");
			TMPConverter.ReplaceAllText(MP_Chatbox);
		}
	}
}
