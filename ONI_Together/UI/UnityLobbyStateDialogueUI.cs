using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Transport.Steamworks;
using ONI_Together.UI.Components;
using ONI_Together.UI.lib.FUI;
using Shared.Helpers;
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
using static ONI_Together.STRINGS.UI;
using static ONI_Together.STRINGS.UI.MP_LOBBY_STATE_DIALOGUE;
using static ONI_Together.STRINGS.UI.MP_PASSWORD_DIALOGUE.HOSTMENU.BUTTONS;
using static ONI_Together.STRINGS.UI.MP_SCREEN.HOSTMENU;
using static ONI_Together.STRINGS.UI.MP_SCREEN.MAINMENU;
using static ONI_Together.STRINGS.UI.PAUSESCREEN;
using static PathFinder;

namespace ONI_Together.UI
{
	internal class UnityLobbyStateDialogueUI : FScreen
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

		public static UnityLobbyStateDialogueUI Instance;

		FButton Close;
		FInputField2 LobbyCode;
		FButton CopyLobbyCode;
		LocText PlayerCountInfo;
		FButton InviteFriends;
		FButton PerformHardSync;
		FButton EndSession;
		GameObject CopyIconReg, CopyIconConfirmed;
		LocText HardSyncText;
		GameObject LobbyCodeContainer, LobbyCodeTitle;

		bool init = false;
		static string lastScene = string.Empty;

		public void Init()
		{
			using var _ = Profiler.Scope();

			if (init) { return; }

			Debug.Log("Initializing UnityLobbyStateDialogueUI");
			Close = transform.Find("TopBar/CloseButton").gameObject.AddOrGet<FButton>();
			Close.OnClick += () => Show(false);
			LobbyCodeTitle = transform.Find("LobbyCodeTitle").gameObject;
			LobbyCodeContainer = transform.Find("LobbyCode").gameObject;
			LobbyCode = LobbyCodeContainer.AddOrGet<FInputField2>();
			CopyLobbyCode = transform.Find("LobbyCode/CopyLobbyCodeButton").gameObject.AddOrGet<FButton>();
			CopyLobbyCode.OnClick += CopyLobbyCodeToClipboard;
			PlayerCountInfo = transform.Find("ConnectedPlayersState").gameObject.GetComponent<LocText>();
			InviteFriends = transform.Find("InviteFriends").gameObject.AddOrGet<FButton>();
			InviteFriends.OnClick += () => Steamworks.SteamFriends.ActivateGameOverlayInviteDialog(SteamLobby.CurrentLobby);
			PerformHardSync = transform.Find("PerformSync").gameObject.AddOrGet<FButton>();
			PerformHardSync.OnClick += DoHardSync;
			HardSyncText = transform.Find("PerformSync/Text").gameObject.GetComponent<LocText>();
			EndSession = transform.Find("EndSession").gameObject.AddOrGet<FButton>();
			EndSession.OnClick += DoEndSession;
			CopyIconReg = transform.Find("LobbyCode/CopyLobbyCodeButton/Image").gameObject;
			CopyIconConfirmed = transform.Find("LobbyCode/CopyLobbyCodeButton/CopyConfirmed").gameObject;
			init = true;
		}
		void DoEndSession()
		{
			using var _ = Profiler.Scope();

			NetworkConfig.Stop();
			Show(false);
			//SpeedControlScreen.Instance?.Unpause(false);
		}
		void CopyLobbyCodeToClipboard()
		{
			using var _ = Profiler.Scope();

			GUIUtility.systemCopyBuffer = SteamLobby.CurrentLobbyCode;
			SetLobbyCodeConfirmationIcon(true);
		}
		void DoHardSync()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.ConnectedPlayers.Count > 0)
			{
				GameServerHardSync.PerformHardSync(true);
			}
			else
			{
				GameServerHardSync.hardSyncDoneThisCycle = true;
			}
			Show(false);
		}
		void RefreshHardSyncLabel()
		{
			using var _ = Profiler.Scope();

			bool hardSyncAlreadyDone = GameServerHardSync.hardSyncDoneThisCycle;
			HardSyncText.SetText(hardSyncAlreadyDone ? HARDSYNCNOTAVAILABLE.LABEL : DOHARDSYNC.LABEL);
			PerformHardSync.SetInteractable(!hardSyncAlreadyDone);
		}
		void SetLobbyCodeConfirmationIcon(bool confirmed)
		{
			using var _ = Profiler.Scope();

			if (ResettingCopyButton != null)
				StopCoroutine(ResettingCopyButton);

			CopyIconReg.SetActive(!confirmed);
			CopyIconConfirmed.SetActive(confirmed);
			if (confirmed)
				ResettingCopyButton = StartCoroutine(RestoreCopyButtonIconAfterDelay());
		}
		IEnumerator RestoreCopyButtonIconAfterDelay()
		{
			using var _ = Profiler.Scope();

			yield return new WaitForSecondsRealtime(1);
			SetLobbyCodeConfirmationIcon(false);
		}
		Coroutine ResettingCopyButton = null;

		public static void ShowLobbyStateWindow()
		{
			using var _ = Profiler.Scope();

			ShowWindow();
			Instance.SetLobbyStateInfo();
		}

		void SetLobbyStateInfo()
		{
			using var _ = Profiler.Scope();

			bool inSteamLobby = SteamLobby.InLobby;

			LobbyCodeTitle.SetActive(inSteamLobby);
			LobbyCodeContainer.SetActive(inSteamLobby);
			InviteFriends.gameObject.SetActive(inSteamLobby);

			if (inSteamLobby)
			{
				LobbyCode.SetTextFromData((SteamLobby.CurrentLobbyCode));
				SetLobbyCodeConfirmationIcon(false);
			}

			RefreshHardSyncLabel();
			PlayerCountInfo.SetText(string.Format(SERVERBROWSER.CONNECTED_PLAYERS, MultiplayerSession.ConnectedPlayers.Count + 1));
		}

		static void ShowWindow()
		{
			using var _ = Profiler.Scope();

			string currentScene = App.GetCurrentSceneName();
			if (currentScene != lastScene)
				OnSceneChanged();
			lastScene = currentScene;
			if (Instance == null)
			{
				var screen = Util.KInstantiateUI(ModAssets.MP_LobbyState_Dialogue, ModAssets.ParentScreen, true);
				Instance = screen.AddOrGet<UnityLobbyStateDialogueUI>();
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
