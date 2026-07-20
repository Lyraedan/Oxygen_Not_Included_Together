using Delaunay.Geo;
using Epic.OnlineServices;
using Klei.CustomSettings;
using NodeEditorFramework;
using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Transport.Steamworks;
using ONI_Together.Patches.ToolPatches;
using ONI_Together.UI.Components;
using ONI_Together.UI.lib;
using PeterHan.PLib.Options;
using Shared.Helpers;
using Shared.Profiling;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UI.lib.UI.FUI;
using UI.lib.UIcmp;
using UnityEngine;
using static ONI_Together.STRINGS.UI;
using static ONI_Together.STRINGS.UI.MP_SCREEN.HOSTMENU;
using static ONI_Together.STRINGS.UI.MP_SCREEN.HOSTMENU.LOBBYSIZE;
using static ONI_Together.STRINGS.UI.PAUSESCREEN;
using static ONI_Together.UI.UnityMultiplayerScreen;

namespace ONI_Together.UI
{
	internal class UnityMultiplayerScreen : FScreen
	{
		enum JoinMode
		{
			Steam,
			Code,
			LAN
		}
		enum HostMode
		{
			Steam,
			LAN
		}

		public static void OnSceneChanged()
		{
			using var _ = Profiler.Scope();

			if (Instance != null)
			{
				UnityEngine.Object.Destroy(Instance.gameObject);
				Instance = null;
			}
		}

		public static UnityMultiplayerScreen Instance;
		bool ShowMain, ShowLobbies, ShowHost, ShowAdditionalHostSettings;

		//Main Areas
		GameObject MainMenuSegment;
		GameObject StartHostingSegment;
		GameObject LobbyBrowserSegment;
		GameObject AdditionalHostSettingsSegment;
		GameObject MiddleSpacer;
		FButton CloseBtn;


		//MainMenuSegment:
		FButton
			HostGame,
			MainCancel;
		//Tabs for Joining
		FToggleButton SteamTabToggle, CodeTabToggle, LanTabToggle;
		JoinMode CurrentJoinMode = JoinMode.Steam;
		//TabContainer:
		GameObject SteamTab, CodeTab, LanTab;
		//SteamTab:
		FButton JoinViaSteam, OpenLobbyBrowser;
		//CodeTab:
		FInputField2 LobbyCodeInput;
		FButton JoinViaCode;
		//LanTab:
		FInputField2 JoinIPInput, JoinPortInput;
		FButton JoinViaLan;

		//HostStartLobbySegment:
		FInputField2 LobbySize;
		FButton AdditionalLobbySettings;
		FButton StartHosting, HostCancel;

		HostMode CurrentHostMode = HostMode.Steam;
		//Tabs for Hosting
		FToggleButton HostSteamToggle, HostLanToggle;
		//TabContainer:
		GameObject HostSteamTab, HostLanTab;
		//SteamTab:
		FToggle PrivateLobbyCheckbox;
		LocText FriendsOnlyStateInfo;
		FButton IncreaseSize, DecreaseSize;
		FInputField2 PasswortInput;
		//LanTab:
		FInputField2 HostIPInput, HostPortInput;

		//LobbyBrowserSegment:
		FButton RefreshLobbiesBtn;
		FInputField2 LobbyFilter;
		GameObject LobbyListContainer;
		LobbyEntryUI LobbyEntryPrefab;
		Dictionary<LobbyListEntry, LobbyEntryUI> Lobbies = [];

		//AdditionalLobbySettingsSegment
		GameObject TogglePrefab, CyclePrefab;
		GameObject SettingsContainer;
		Dictionary<string, FToggle> SettingsToggles = [];
		Dictionary<string, FCycle> SettingsCycles = [];

		Callback<LobbyDataUpdate_t> lobbyDataCallback;

		bool init = false;
		static string lastScene = string.Empty;
		Coroutine LobbyRefresh;
		ulong _pendingLobbyId = Utils.NilUlong();

		public void Init()
		{
			using var _ = Profiler.Scope();

			if (init) { return; }

			lobbyDataCallback = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdateReceived);

			Debug.Log("Initializing MultiplayerScreen");

			///init main area of base screen
			MainMenuSegment = transform.Find("MainMenu").gameObject;
			StartHostingSegment = transform.Find("HostMenu").gameObject;
			LobbyBrowserSegment = transform.Find("LobbyList").gameObject;
			AdditionalHostSettingsSegment = transform.Find("AdditionalHostSettings").gameObject;
			MiddleSpacer = transform.Find("MainSpacer").gameObject;
			CloseBtn = transform.Find("TopBar/CloseButton").gameObject.AddOrGet<FButton>();
			CloseBtn.OnClick += () => Show(false);

			HostGame = transform.Find("MainMenu/HostGameButton").gameObject.AddOrGet<FButton>();
			HostGame.OnClick += () => ShowHostSegment(true);
			MainCancel = transform.Find("MainMenu/Cancel").gameObject.AddOrGet<FButton>();
			MainCancel.OnClick += () => Show(false);
			///init tabs
			SteamTabToggle = transform.Find("MainMenu/JoinViaButtons/Steam").gameObject.AddOrGet<FToggleButton>();
			SteamTabToggle.OnClick += () => SetJoinVia(JoinMode.Steam);
			CodeTabToggle = transform.Find("MainMenu/JoinViaButtons/Code").gameObject.AddOrGet<FToggleButton>();
			CodeTabToggle.OnClick += () => SetJoinVia(JoinMode.Code);
			LanTabToggle = transform.Find("MainMenu/JoinViaButtons/LAN").gameObject.AddOrGet<FToggleButton>();
			LanTabToggle.OnClick += () => SetJoinVia(JoinMode.LAN);

			SteamTab = transform.Find("MainMenu/SteamJoin").gameObject;
			CodeTab = transform.Find("MainMenu/LobbyCodeJoin").gameObject;
			LanTab = transform.Find("MainMenu/LanJoin").gameObject;
			///init Steam Join Tab
			JoinViaSteam = transform.Find("MainMenu/SteamJoin/JoinViaSteam").gameObject.AddOrGet<FButton>();
			JoinViaSteam.OnClick += () => SteamFriends.ActivateGameOverlay("friends");
			OpenLobbyBrowser = transform.Find("MainMenu/SteamJoin/OpenLobbyListButton").gameObject.AddOrGet<FButton>();
			OpenLobbyBrowser.OnClick += () => ShowLobbySegment(true);
			///init Code Join Tab
			JoinViaCode = transform.Find("MainMenu/LobbyCodeJoin/JoinWithCodeButton").gameObject.AddOrGet<FButton>();
			JoinViaCode.OnClick += JoinLobbyWithCode;
			LobbyCodeInput = transform.Find("MainMenu/LobbyCodeJoin/Input").FindOrAddComponent<FInputField2>();
			LobbyCodeInput.Text = string.Empty;
			LobbyCodeInput.inputField.characterLimit = 16;
			///init LAN Join Tab
			JoinIPInput = transform.Find("MainMenu/LanJoin/Inputs/IpInput").FindOrAddComponent<FInputField2>();
			JoinIPInput.Text = string.Empty;
			JoinPortInput = transform.Find("MainMenu/LanJoin/Inputs/Port").FindOrAddComponent<FInputField2>();
			JoinPortInput.Text = "8080";
			JoinViaLan = transform.Find("MainMenu/LanJoin/JoinLANButton").gameObject.AddOrGet<FButton>();
			JoinViaLan.OnClick += JoinLanLobby;

			// Load last used LAN settings
			if (!string.IsNullOrEmpty(Configuration.Instance.Client.LanSettings.Ip))
			{
				JoinIPInput.Text = Configuration.Instance.Client.LanSettings.Ip;
				JoinPortInput.Text = Configuration.Instance.Client.LanSettings.Port.ToString();
			}

			HostSteamTab = transform.Find("HostMenu/SteamHosting").gameObject;
			HostLanTab = transform.Find("HostMenu/LanHosting").gameObject;
			HostSteamToggle = transform.Find("HostMenu/HostViaButtons/Steam").gameObject.AddOrGet<FToggleButton>();
			HostSteamToggle.OnClick += () => SetHostVia(HostMode.Steam);
			HostLanToggle = transform.Find("HostMenu/HostViaButtons/LAN").gameObject.AddOrGet<FToggleButton>();
			HostLanToggle.OnClick += () => SetHostVia(HostMode.LAN);

			HostIPInput = transform.Find("HostMenu/LanHosting/IpTarget/Input").FindOrAddComponent<FInputField2>();
			HostIPInput.Text = "127.0.0.1";
			HostPortInput = transform.Find("HostMenu/LanHosting/Port/Input").FindOrAddComponent<FInputField2>();
			HostPortInput.Text = "8080";

			if (!string.IsNullOrEmpty(Configuration.Instance.Host.LanSettings.Ip))
			{
				HostIPInput.Text = Configuration.Instance.Host.LanSettings.Ip;
				HostPortInput.Text = Configuration.Instance.Host.LanSettings.Port.ToString();
			}


			LobbySize = transform.Find("HostMenu/LobbySize/LobbySizeInput").gameObject.AddOrGet<FInputField2>();
			LobbySize.Text = NetworkConfig.LOBBY_SIZE_DEFAULT.ToString();

			if (!string.IsNullOrEmpty(Configuration.Instance.Host.MaxLobbySize.ToString()))
			{
				LobbySize.Text = Configuration.Instance.Host.MaxLobbySize.ToString();
			}

			LobbySize.OnValueChanged.AddListener(ClampLobbySize);
			IncreaseSize = transform.Find("HostMenu/LobbySize/LobbySizeInput/Increase").gameObject.AddOrGet<FButton>();
			IncreaseSize.OnClick += IncreaseLobbySize;
			DecreaseSize = transform.Find("HostMenu/LobbySize/LobbySizeInput/Decrease").gameObject.AddOrGet<FButton>();
			DecreaseSize.OnClick += DecreaseLobbySize;


			FriendsOnlyStateInfo = transform.Find("HostMenu/SteamHosting/FriendsOnly/State").gameObject.GetComponent<LocText>();
			PrivateLobbyCheckbox = transform.Find("HostMenu/SteamHosting/FriendsOnly/Checkbox").gameObject.AddOrGet<FToggle>();
			PrivateLobbyCheckbox.SetCheckmark("Checkmark");
			PrivateLobbyCheckbox.SetOnFromCode(true);
			TintLobbyState(true);
			PrivateLobbyCheckbox.OnChange += (on) => TintLobbyState(on);

			PasswortInput = transform.Find("HostMenu/SteamHosting/PasswordInput").gameObject.AddOrGet<FInputField2>();
			PasswortInput.Text = string.Empty;

			AdditionalLobbySettings = transform.Find("HostMenu/AdditionalSettings").gameObject.AddOrGet<FButton>();
			AdditionalLobbySettings.SetInteractable(true);
			AdditionalLobbySettings.OnClick += ToggleAdditionalHostSettingsSegment;
			//UIUtils.AddSimpleTooltipToObject(AdditionalLobbySettings.gameObject, WORK_IN_PROGRESS);

			StartHosting = transform.Find("HostMenu/Buttons/StartHosting").gameObject.AddOrGet<FButton>();
			StartHosting.OnClick += () => StartHostingGame();
			HostCancel = transform.Find("HostMenu/Buttons/Cancel").gameObject.AddOrGet<FButton>();
			HostCancel.OnClick += () => CancelHosting();

			RefreshLobbiesBtn = transform.Find("LobbyList/SearchBar/RefreshButton").gameObject.AddOrGet<FButton>();
			RefreshLobbiesBtn.OnClick += () => RefreshLobbies();
			LobbyFilter = transform.Find("LobbyList/SearchBar/Input").gameObject.AddOrGet<FInputField2>();
			LobbyFilter.Text = string.Empty;
			LobbyListContainer = transform.Find("LobbyList/ScrollArea/Content").gameObject;

			SettingsContainer = transform.Find("AdditionalHostSettings/ScrollArea/Content").gameObject;
			TogglePrefab = transform.Find("AdditionalHostSettings/ScrollArea/Content/TogglePrefab").gameObject;
			TogglePrefab.SetActive(false);
			CyclePrefab = transform.Find("AdditionalHostSettings/ScrollArea/Content/SwitchPrefab").gameObject;
			CyclePrefab.SetActive(false);


			var entryPrefabGO = transform.Find("LobbyList/ScrollArea/Content/EntryPrefab").gameObject;
			entryPrefabGO.SetActive(false);
			LobbyEntryPrefab = entryPrefabGO.AddOrGet<LobbyEntryUI>();
			RefreshLobbySizeButtons();

			SetJoinVia(JoinMode.Steam);
			SetHostVia(HostMode.Steam);
			NetworkConfig.UpdateTransport(NetworkConfig.NetworkTransport.STEAMWORKS); // default to steam

			init = true;
		}

		private void SetHostVia(HostMode current)
		{
			CurrentHostMode = current;

			HostLanTab.SetActive(current == HostMode.LAN);
			HostSteamTab.SetActive(current == HostMode.Steam);

			HostLanToggle.SetIsSelected(current == HostMode.LAN);
			HostSteamToggle.SetIsSelected(current == HostMode.Steam);
		}

		private void SetJoinVia(JoinMode current)
		{
			CurrentJoinMode = current;

			SteamTab.SetActive(current == JoinMode.Steam);
			CodeTab.SetActive(current == JoinMode.Code);
			LanTab.SetActive(current == JoinMode.LAN);

			SteamTabToggle.SetIsSelected(current == JoinMode.Steam);
			CodeTabToggle.SetIsSelected(current == JoinMode.Code);
			LanTabToggle.SetIsSelected(current == JoinMode.LAN);
		}

		void IncreaseLobbySize()
		{
			using var _ = Profiler.Scope();

			if (int.TryParse(LobbySize.Text, out int lobbySize))
			{
				lobbySize++;
				lobbySize = Mathf.Clamp(lobbySize, NetworkConfig.LOBBY_SIZE_MIN, NetworkConfig.LOBBY_SIZE_MAX);
				LobbySize.SetTextFromData(lobbySize.ToString());
				RefreshLobbySizeButtons();

				Configuration.Instance.Host.MaxLobbySize = lobbySize;
				Configuration.Instance.Save();
			}
		}

		void DecreaseLobbySize()
		{
			using var _ = Profiler.Scope();

			if (int.TryParse(LobbySize.Text, out int lobbySize))
			{
				lobbySize--;
				lobbySize = Mathf.Clamp(lobbySize, NetworkConfig.LOBBY_SIZE_MIN, NetworkConfig.LOBBY_SIZE_MAX);
				LobbySize.SetTextFromData(lobbySize.ToString());
				RefreshLobbySizeButtons();

				Configuration.Instance.Host.MaxLobbySize = lobbySize;
				Configuration.Instance.Save();
			}
		}
		void RefreshLobbySizeButtons()
		{
			using var _ = Profiler.Scope();

			if (!int.TryParse(LobbySize.Text, out int lobbySize))
				lobbySize = NetworkConfig.LOBBY_SIZE_DEFAULT;

			IncreaseSize.SetInteractable(lobbySize < NetworkConfig.LOBBY_SIZE_MAX);
			DecreaseSize.SetInteractable(lobbySize > NetworkConfig.LOBBY_SIZE_MIN);
		}
		void CancelHosting()
		{
			using var _ = Profiler.Scope();

			if (ShowMain)
				ShowHostSegment(false);
			else
				Show(false);
		}
		void ClampLobbySize(string text)
		{
			using var _ = Profiler.Scope();

			if (int.TryParse(text, out int lobbySize))
			{
				lobbySize = Mathf.Clamp(lobbySize, NetworkConfig.LOBBY_SIZE_MIN, NetworkConfig.LOBBY_SIZE_MAX);
				LobbySize.SetTextFromData(lobbySize.ToString());
			}
			else
				LobbySize.SetTextFromData(NetworkConfig.LOBBY_SIZE_DEFAULT.ToString());
			RefreshLobbySizeButtons();
		}

		public static void ShowWindow()
		{
			using var _ = Profiler.Scope();

			string currentScene = App.GetCurrentSceneName();
			if (currentScene != lastScene)
				OnSceneChanged();
			lastScene = currentScene;
			if (Instance == null)
			{
				var screen = Util.KInstantiateUI(ModAssets.MP_ScreenPrefab, ModAssets.ParentScreen, true);
				Instance = screen.AddOrGet<UnityMultiplayerScreen>();
				Instance.Init();
			}
			Instance.Show(true);
			Instance.ConsumeMouseScroll = true;
			Instance.transform.SetAsLastSibling();
		}
		public override void OnShow(bool show)
		{
			using var _ = Profiler.Scope();

			base.OnShow(show);

			if (show)
				LobbyRefresh = StartCoroutine(RefreshLobbiesEnumerator());
			else
				StopCoroutine(LobbyRefresh);
		}

		public static void OpenFromMainMenu()
		{
			using var _ = Profiler.Scope();

			ShowWindow();
			Instance.ShowMainSegment(true);
			Instance.ShowHostSegment(false);
			Instance.ShowLobbySegment(false);
			Instance.ShowAdditionalHostSettingsSegment(false);
		}
		public static void OpenFromPauseScreen()
		{
			using var _ = Profiler.Scope();

			ShowWindow();
			Instance.ShowMainSegment(false);
			Instance.ShowLobbySegment(false);
			Instance.ShowHostSegment(true);
			Instance.ShowAdditionalHostSettingsSegment(false);
		}

		void JoinLanLobby()
		{
			using var _ = Profiler.Scope();
			NetworkConfig.UpdateTransport(NetworkConfig.NetworkTransport.RIPTIDE);

			string ipAdress = JoinIPInput.Text;
			string portText = JoinPortInput.Text;

			if (int.TryParse(portText, out int port))
			{
				Configuration.Instance.Client.LanSettings.Ip = ipAdress;
				Configuration.Instance.Client.LanSettings.Port = int.Parse(portText);
				Configuration.Instance.Save();

				Debug.Log("Trying to join LAN lobby with IP: " + ipAdress + " and Port: " + portText);
				GameClient.ConnectToHost(ip: ipAdress, port: port);
			}

		}

		void JoinLobbyWithCode()
		{
			using var _ = Profiler.Scope();

			// First step: Validate and parse code
			string code = LobbyCodeHelper.CleanCode(LobbyCodeInput.Text);

			if (string.IsNullOrEmpty(code))
			{
				DialogUtil.CreateConfirmDialogFrontend(JOINBYDIALOGMENU.JOIN_BY_CODE, STRINGS.UI.JOINBYDIALOGMENU.ERR_ENTER_CODE);
				return;
			}

			if (!LobbyCodeHelper.IsValidCodeFormat(code))
			{
				DialogUtil.CreateConfirmDialogFrontend(JOINBYDIALOGMENU.JOIN_BY_CODE, STRINGS.UI.JOINBYDIALOGMENU.ERR_INVALID_CODE);
				return;
			}

			if (!LobbyCodeHelper.TryParseCode(code, out ulong lobbyId))
			{
				DialogUtil.CreateConfirmDialogFrontend(JOINBYDIALOGMENU.JOIN_BY_CODE, STRINGS.UI.JOINBYDIALOGMENU.ERR_PARSE_CODE_FAILED);
				return;
			}

			NetworkConfig.UpdateTransport(NetworkConfig.NetworkTransport.STEAMWORKS); // joining via steam code
			_pendingLobbyId = lobbyId;

			// We need to join the lobby to get its metadata (including password status)
			// But first, let's check if we can get the data by requesting lobby data
			SteamMatchmaking.RequestLobbyData(lobbyId.AsCSteamID());
		}

		void OnLobbyDataUpdateReceived(LobbyDataUpdate_t data)
		{
			using var _ = Profiler.Scope();

			if (data.m_ulSteamIDLobby != _pendingLobbyId)
				return;

			if (data.m_bSuccess == 0)
				return;

			JoinOrOpenPasswordDialogue(_pendingLobbyId);
			_pendingLobbyId = Utils.NilUlong();
		}

		void JoinOrOpenPasswordDialogue(ulong lobbyId)
		{
			using var _ = Profiler.Scope();

			if (!SteamLobby.TryGetLobbyPasswordRequirement(
				    lobbyId.AsCSteamID(), out bool hasPassword))
			{
				DebugConsole.LogWarning($"[LobbyBrowser] Missing access metadata for lobby {lobbyId}");
				return;
			}

			if (!hasPassword)
				JoinSteamLobby(lobbyId);
			else
				OpenPasswordDialogue(lobbyId);

		}
		void JoinSteamLobby(ulong lobbyId)
		{
			using var _ = Profiler.Scope();
			NetworkConfig.UpdateTransport(NetworkConfig.NetworkTransport.STEAMWORKS);

			SteamLobby.JoinLobby(lobbyId.AsCSteamID(), (lobbyId) =>
			{
				DebugConsole.Log($"[LobbyBrowser] Successfully joined lobby: {lobbyId}");
				this.Show(false);
			});
		}
		void OpenPasswordDialogue(ulong lobbyId)
		{
			using var _ = Profiler.Scope();

			UnityPasswordInputDialogueUI.ShowPasswordDialogueFor(lobbyId);
		}


		void ShowMainSegment(bool show)
		{
			using var _ = Profiler.Scope();

			ShowMain = show;
			MainMenuSegment.SetActive(show);
			RefreshSpacer();
		}
		void ShowHostSegment(bool show)
		{
			using var _ = Profiler.Scope();

			if (ShowLobbies && show)
				ShowLobbySegment(false);
			ShowHost = show;
			StartHostingSegment.SetActive(show);
			if (ShowAdditionalHostSettings && !show)
				ShowAdditionalHostSettingsSegment(false);
			RefreshSpacer();
		}
		void ToggleAdditionalHostSettingsSegment()
		{
			using var _ = Profiler.Scope();
			ShowAdditionalHostSettingsSegment(!ShowAdditionalHostSettings);
		}
		void ShowAdditionalHostSettingsSegment(bool show)
		{
			using var _ = Profiler.Scope();

			ShowAdditionalHostSettings = show;
			AdditionalHostSettingsSegment.SetActive(show);
			if (show)
				ShowLobbySettings();
		}
		void ShowLobbySegment(bool show)
		{
			using var _ = Profiler.Scope();

			if (ShowHost && show)
			{
				ShowHostSegment(false);
			}
			ShowLobbies = show;
			LobbyBrowserSegment.SetActive(show);
			RefreshSpacer();
			if (show)
				RefreshLobbies();
		}

		static Color PublicLobbyTint = new Color(0.4f, 1f, 0.6f), PrivateLobbyTint = new Color(1f, 0.8f, 0.4f);

		void TintLobbyState(bool isPrivate)
		{
			using var _ = Profiler.Scope();

			//string text = isPrivate ? STRINGS.UI.MP_SCREEN.HOSTMENU.FRIENDSONLY.LOBBY_VISIBILITY_FRIENDSONLY : STRINGS.UI.MP_SCREEN.HOSTMENU.FRIENDSONLY.LOBBY_VISIBILITY_PUBLIC;
			//LobbyStateInfo.SetText(Utils.ColorText(text, isPrivate ? PrivateLobbyTint : PublicLobbyTint));
			string text = isPrivate ? STRINGS.UI.FRIENDSONLYMODE.LOBBY_VISIBILITY_FRIENDSONLY : STRINGS.UI.FRIENDSONLYMODE.LOBBY_VISIBILITY_PUBLIC;
			FriendsOnlyStateInfo.SetText(Utils.ColorText(text, isPrivate ? PrivateLobbyTint : PublicLobbyTint));
		}


		void RefreshSpacer()
		{
			using var _ = Profiler.Scope();

			MiddleSpacer.SetActive(ShowMain && (ShowLobbies || ShowHost));
		}

		int secondsPassed = 0;
		IEnumerator RefreshLobbiesEnumerator()
		{
			using var _ = Profiler.Scope();

			for (; ; )
			{
				RefreshLobbies();
				yield return new WaitForSeconds(10);
			}
		}

		void RefreshLobbies()
		{
			using var _ = Profiler.Scope();

			if (!ShowLobbies)
				return;

			SteamLobby.RequestLobbyList(OnLobbyListReceived);
		}
		private void OnLobbyListReceived(List<LobbyListEntry> lobbies)
		{
			using var _ = Profiler.Scope();

			foreach (var existing in Lobbies.Values)
			{
				existing.Hide();
			}
			foreach (var current in lobbies)
			{
				var entry = AddOrGetLobbyEntryUI(current);
				entry.RefreshDisplayedInfo();
			}
		}

		LobbyEntryUI AddOrGetLobbyEntryUI(LobbyListEntry lobby)
		{
			using var _ = Profiler.Scope();

			if (Lobbies.TryGetValue(lobby, out LobbyEntryUI entryUI))
			{
				entryUI.gameObject.SetActive(true);
				return entryUI;
			}
			entryUI = Util.KInstantiateUI<LobbyEntryUI>(LobbyEntryPrefab.gameObject, LobbyListContainer);
			entryUI.gameObject.SetActive(true);
			entryUI.SetLobby(lobby);
			entryUI.SetJoinFunction(OnLobbyJoinClicked);
			Lobbies[lobby] = entryUI;
			return entryUI;
		}

		void ShowLobbySettings()
		{
			AddOrGetLobbySettingsEntry_Toggle("HardSyncToggle", ToggleHardSyncSetting,
				STRINGS.UI.CONFIGURATION.TITLES.HOST_SETTINGS.SERVER_SETTINGS.HARD_SYNC_AT_CYCLE_START,
				STRINGS.UI.CONFIGURATION.TOOLTIPS.HOST_SETTINGS.SERVER_SETTINGS.HARD_SYNC_AT_CYCLE_START)
				.SetOnFromCode(Configuration.Instance.Host.Server.HardSyncAtCycleStart);

			AddOrGetLobbySettingsEntry_Toggle("PauseSimOnPlayerLeft", TogglePauseSimOnPlayerLeftSetting,
				STRINGS.UI.CONFIGURATION.TITLES.HOST_SETTINGS.SERVER_SETTINGS.PAUSE_SIM_ON_PLAYER_DISCONNECT,
				STRINGS.UI.CONFIGURATION.TOOLTIPS.HOST_SETTINGS.SERVER_SETTINGS.PAUSE_SIM_ON_PLAYER_DISCONNECT)
				.SetOnFromCode(Configuration.Instance.Host.Server.PauseSimOnPlayerDisconnect);
		}
		
		void ToggleHardSyncSetting(bool hardSyncEnabled)
		{
			var config = Configuration.Instance;
			config.Host.Server.HardSyncAtCycleStart = hardSyncEnabled;
			config.Save();
		}

		void TogglePauseSimOnPlayerLeftSetting(bool enabled)
		{
			var config = Configuration.Instance;
			config.Host.Server.PauseSimOnPlayerDisconnect = enabled;
			config.Save();
		}

		public FToggle AddOrGetLobbySettingsEntry_Toggle(string id, System.Action<bool> onToggleChange, string label, string tooltip = "")
		{
			if (!SettingsToggles.TryGetValue(id, out var optionToggle))
			{
				var toggle = Util.KInstantiateUI(TogglePrefab, SettingsContainer, true).gameObject.AddOrGet<FToggle>();

				var settingLabel = toggle.transform.Find("Label").gameObject.AddOrGet<LocText>();
				settingLabel.text = label;
				if (tooltip.Any())
					UIUtils.AddSimpleTooltipToObject(settingLabel.transform, tooltip, alignCenter: true, onBottom: true);

				toggle.SetCheckmark("Background/Checkmark");
				toggle.OnClick += onToggleChange;
				optionToggle = SettingsToggles[id] = toggle;
			}
			optionToggle.gameObject.SetActive(true);
			return optionToggle;
		}
		public FCycle AddOrGetLobbySettingsEntry_Cycle(string id, List<FCycle.Option> options, System.Action<FCycle.Option> onOptionSelect, string label, string tooltip = "")
		{
			if (!SettingsCycles.TryGetValue(id, out var optionCycle))
			{
				var cycle = Util.KInstantiateUI(CyclePrefab, SettingsContainer, true).AddOrGet<FCycle>();

				cycle.SetDescriptionFormatter(desc => desc.Replace("\n\n", "\n"));
				var settingLabel = cycle.transform.Find("Label").gameObject.AddOrGet<LocText>();
				settingLabel.text = label;
				if (tooltip.Any())
					UIUtils.AddSimpleTooltipToObject(settingLabel.transform, tooltip, alignCenter: true, onBottom: true);

				cycle.Initialize(
					cycle.transform.Find("Left").gameObject.AddOrGet<FButton>(),
					cycle.transform.Find("Right").gameObject.AddOrGet<FButton>(),
					cycle.transform.Find("ChoiceLabel").gameObject.AddOrGet<LocText>(),
					cycle.transform.Find("ChoiceLabel/Description").gameObject.AddOrGet<LocText>());

				cycle.Options = options;
				cycle.OnChange += onOptionSelect;
				optionCycle = SettingsCycles[id] = cycle;
			}
			optionCycle.gameObject.SetActive(true);
			return optionCycle;
		}
		void OnLobbyJoinClicked(LobbyListEntry lobby)
		{
			using var _ = Profiler.Scope();

			if (lobby.HasPassword)
			{
				OpenPasswordDialogue(lobby.LobbyId);
			}
			else
			{
				// Direct join
				JoinSteamLobby(lobby.LobbyId);
			}
		}

		void StoreHostConfigurationSettings()
		{
			using var _ = Profiler.Scope();

			Configuration.Instance.Host.Lobby.IsPrivate = PrivateLobbyCheckbox.On;
			string lobbySize = LobbySize.Text;
			if (lobbySize.Any())
			{
				if (!int.TryParse(lobbySize, out int maxLobbySize))
					maxLobbySize = NetworkConfig.LOBBY_SIZE_MIN;
				maxLobbySize = Mathf.Clamp(maxLobbySize, NetworkConfig.LOBBY_SIZE_MIN, NetworkConfig.LOBBY_SIZE_MAX);
				Configuration.Instance.Host.MaxLobbySize = maxLobbySize;
			}
			else
			{
				Configuration.Instance.Host.MaxLobbySize = NetworkConfig.LOBBY_SIZE_DEFAULT;
			}

			string password = PasswortInput.Text;
			if (password.Any())
			{
				Configuration.Instance.Host.Lobby.RequirePassword = true;
				Configuration.Instance.Host.Lobby.PasswordHash = PasswordHelper.HashPassword(password);
			}
			else
			{
				Configuration.Instance.Host.Lobby.RequirePassword = false;
				Configuration.Instance.Host.Lobby.PasswordHash = string.Empty;
			}
			Configuration.Instance.Save();
		}

		void StartHostingGame()
		{
            switch (CurrentHostMode)
			{
				case HostMode.Steam:
					StartHostingSteamGame();
					break;
				case HostMode.LAN:
					StartHostingLanGame();
					break;
			}
		}
		private void StartHostingLanGame()
		{
			using var _ = Profiler.Scope();
            NetworkConfig.UpdateTransport(NetworkConfig.NetworkTransport.RIPTIDE);

            string ipAdress = HostIPInput.Text;
			string portText = HostPortInput.Text;

			if (int.TryParse(portText, out int port))
			{
				Configuration.Instance.Host.LanSettings.Ip = ipAdress;
				Configuration.Instance.Host.LanSettings.Port = port;
				Configuration.Instance.Save();

				string lobbySize = LobbySize.Text;
				if (lobbySize.Any())
				{
					if (!int.TryParse(lobbySize, out int maxLobbySize))
						maxLobbySize = NetworkConfig.LOBBY_SIZE_MIN;
					maxLobbySize = Mathf.Clamp(maxLobbySize, NetworkConfig.LOBBY_SIZE_MIN, NetworkConfig.LOBBY_SIZE_MAX);
					Configuration.Instance.Host.MaxLobbySize = maxLobbySize;
				}
				else
				{
					Configuration.Instance.Host.MaxLobbySize = NetworkConfig.LOBBY_SIZE_DEFAULT;
				}

				Debug.Log("Trying to start LAN lobby with IP: " + ipAdress + " and Port: " + portText);

				if (Utils.IsInGame())
				{
					NetworkConfig.StartServer();
				}
				else
				{
					MultiplayerSession.ShouldHostAfterLoad = true; // Set flag to start hosting after loading

					string saveForCurrentDlc = SaveLoader.GetLatestSaveForCurrentDLC();
					bool hasVersionCompatibleSave = !string.IsNullOrEmpty(saveForCurrentDlc) && System.IO.File.Exists(saveForCurrentDlc);
					if (hasVersionCompatibleSave)
					{
						DebugConsole.Log($"[UnityMultiplayerScreen/StartHostingGame] Found existing compatible savefile. Opening load sequence");
						MainMenu.Instance?.LoadGame();
						RegisterOnExitLoadScreenTriggers();
					}
					else
					{
						DebugConsole.Log("$[UnityMultiplayerScreen/StartHostingGame] No saves found! Running new game sequence.");
						MainMenu.Instance?.NewGame();
					}
				}
				Show(false);
			}
		}

		private void StartHostingSteamGame()
		{
			using var _ = Profiler.Scope();
            NetworkConfig.UpdateTransport(NetworkConfig.NetworkTransport.STEAMWORKS);

            // Save the host config
            StoreHostConfigurationSettings();

			if (Utils.IsInGame())
			{
				NetworkConfig.StartServer();
			}
			else
			{
				MultiplayerSession.ShouldHostAfterLoad = true; // Set flag to start hosting after loading

				string saveForCurrentDlc = SaveLoader.GetLatestSaveForCurrentDLC();
				bool hasVersionCompatibleSave = !string.IsNullOrEmpty(saveForCurrentDlc) && System.IO.File.Exists(saveForCurrentDlc);
				if (hasVersionCompatibleSave)
				{
					DebugConsole.Log($"[UnityMultiplayerScreen/StartHostingGame] Found existing compatible savefile. Opening load sequence");
					MainMenu.Instance?.LoadGame();
					RegisterOnExitLoadScreenTriggers();
				}
				else
				{
					DebugConsole.Log("$[UnityMultiplayerScreen/StartHostingGame] No saves found! Running new game sequence.");
					MainMenu.Instance?.NewGame();
				}
			}
			Show(false);
		}
		void RegisterOnExitLoadScreenTriggers()
		{
			using var _ = Profiler.Scope();

			LoadScreen.Instance.closeButton.onClick += OnLoadScreenExited;
			UI_Patches.OnLoadScreenExited = OnLoadScreenExited;
		}
		void OnLoadScreenExited()
		{
			using var _ = Profiler.Scope();

			UI_Patches.OnLoadScreenExited = null;
			LoadScreen.Instance.closeButton.onClick -= OnLoadScreenExited;
			MultiplayerSession.ShouldHostAfterLoad = false; // Reset the flag if the load screen is closed
			OpenFromMainMenu();
		}
	}
}

