using ONI_MP.DebugTools;
using ONI_MP.Misc;
using ONI_MP.Networking;
using ONI_MP.Networking.Transport.Steamworks;
using ONI_MP.UI.lib.FUI;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;
using UI.lib.UIcmp;
using UnityEngine;
using UnityEngine.UI;
using static Klei.FileUtil;
using static ONI_MP.STRINGS.UI;

namespace ONI_MP.UI
{
	internal class UnityPasswordInputDialogueUI : FScreen
	{
		public static void OnSceneChanged()
		{
			using var _ = Profiler.Scope();

			if (Instance != null)
			{
				UnityEngine.Object.Destroy(Instance.gameObject);
				Instance = null;
			}
		}

		public static UnityPasswordInputDialogueUI Instance;


		//HostStartLobbySegment:
		LocText PasswordStatus;
		FInputField2 PasswortInput;
		FButton Confirm, Cancel, TopbarClose;
		PasswordInputToggle PasswordToggle;

		bool init = false;
		static string lastScene = string.Empty;
		ulong LobbyId;

		public void Init()
		{
			using var _ = Profiler.Scope();

			if (init) { return; }

			TopbarClose = transform.Find("TopBar/CloseButton").gameObject.AddOrGet<FButton>();
			TopbarClose.OnClick += () => Show(false);


			Debug.Log("Initializing UnityPasswordInputDialogueUI");
			PasswortInput = transform.Find("HostMenu/PasswordInput").FindOrAddComponent<FInputField2>();
			PasswortInput.Text = string.Empty;
			PasswordToggle = transform.Find("HostMenu/PasswordInput/TogglePasswordVis").gameObject.AddOrGet<PasswordInputToggle>();
			PasswordToggle.InitEyeToggle(PasswortInput);

			Confirm = transform.Find("HostMenu/Buttons/Confirm").gameObject.AddOrGet<FButton>();
			Cancel = transform.Find("HostMenu/Buttons/Cancel").gameObject.AddOrGet<FButton>();
			Confirm.OnClick += VerifyPasswordInput;
			Cancel.OnClick += () => Show(false);

			PasswordStatus = transform.Find("HostMenu/PasswordTitle").gameObject.GetComponent<LocText>();
			init = true;
		}
		void VerifyPasswordInput()
		{
			using var _ = Profiler.Scope();

			string password = PasswortInput.Text;
			if (SteamLobby.ValidateLobbyPassword(LobbyId, password))
			{
				SetRegularStatus();
				SteamLobby.JoinLobby(LobbyId.AsCSteamID(), (lobbyId) =>
				{
					DebugConsole.Log($"[LobbyBrowser] Successfully joined lobby: {lobbyId}");

				});
				Show(false);
			}
			else
			{
				Instance.PasswordStatus.SetText(Utils.ColorText(MP_PASSWORD_DIALOGUE.HOSTMENU.PASSWORD_INCORRECT, Color.red));
			}
		}
		void SetRegularStatus() => PasswordStatus.SetText(MP_PASSWORD_DIALOGUE.HOSTMENU.PASSWORDTITLE);

		public static void ShowPasswordDialogueFor(ulong lobby)
		{
			using var _ = Profiler.Scope();

			ShowWindow();
			Instance.PasswortInput.Text = string.Empty;
			Instance.HidePw();
			Instance.SetRegularStatus();
			Instance.LobbyId = lobby;
		}
		void HidePw() => PasswordToggle.SetPasswordVisibility(false);

		static void ShowWindow()
		{
			using var _ = Profiler.Scope();

			string currentScene = App.GetCurrentSceneName();
			if (currentScene != lastScene)
				OnSceneChanged();
			lastScene = currentScene;
			if (Instance == null)
			{
				var screen = Util.KInstantiateUI(ModAssets.MP_PW_Dialogue, ModAssets.ParentScreen, true);
				Instance = screen.AddOrGet<UnityPasswordInputDialogueUI>();
				Instance.Init();
			}
			Instance.Show(true);
			Instance.ConsumeMouseScroll = true;
			Instance.transform.SetAsLastSibling();
		}

		public override void OnShow(bool show)
		{
			base.OnShow(show);
		}
	}
}
