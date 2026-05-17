using System;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Transport.Steamworks;
using Shared.Profiling;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ONI_Together.Menus
{
	/// <summary>
	/// Screen for configuring lobby settings before hosting.
	/// Shows options for public/private visibility and password.
	/// </summary>
	public class HostLobbyConfigScreen : MonoBehaviour
	{
		private static HostLobbyConfigScreen _instance;
		private static GameObject _screenGO;

		private Toggle _privateToggle;
		private TMP_InputField _passwordInput;
		private TextMeshProUGUI _privateLabel;
		private TMP_InputField _lobbySizeInput;
		private static System.Action _onContinue;

		public static void Show(Transform parent, System.Action onContinue = null)
		{
			using var _ = Profiler.Scope();

			if (_instance != null)
			{
				DebugConsole.Log("[HostLobbyConfigScreen] Screen already open.");
				return;
			}

			_onContinue = onContinue;
			_screenGO = CreateScreen(parent);
			_instance = _screenGO.AddComponent<HostLobbyConfigScreen>();
			_instance.Initialize();
		}

		public static void Close()
		{
			using var _ = Profiler.Scope();

			if (_screenGO != null)
			{
				Destroy(_screenGO);
				_screenGO = null;
				_instance = null;
			}
		}

		private static GameObject CreateScreen(Transform parent)
		{
			using var _ = Profiler.Scope();

			GameObject screen = new GameObject("HostLobbyConfigScreen", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
			screen.transform.SetParent(parent, false);

			var rt = screen.GetComponent<RectTransform>();
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;

			var image = screen.GetComponent<Image>();
			image.color = new Color(0.05f, 0.05f, 0.08f, 0.95f);

			var canvasGroup = screen.GetComponent<CanvasGroup>();
			canvasGroup.interactable = true;
			canvasGroup.blocksRaycasts = true;

			return screen;
		}

		private bool _wasPlayerControllerEnabled = true;
		private bool _wasCameraControllerEnabled = true;
		private GameObject _planScreenGO;
		private bool _wasPlanScreenActive;

		private void OnEnable()
		{
			using var _ = Profiler.Scope();

			// Disable PlayerController to block player input
			if (PlayerController.Instance != null)
			{
				_wasPlayerControllerEnabled = PlayerController.Instance.enabled;
				PlayerController.Instance.enabled = false;
			}

			// Disable CameraController to block WASD camera movement
			if (CameraController.Instance != null)
			{
				_wasCameraControllerEnabled = CameraController.Instance.enabled;
				CameraController.Instance.enabled = false;
			}

			// Hide ToolMenu
			if (ToolMenu.Instance != null)
			{
				ToolMenu.Instance.gameObject.SetActive(false);
			}

			// Hide PlanScreen (building menu)
			_planScreenGO = null;
			var planScreen = UnityEngine.Object.FindFirstObjectByType<PlanScreen>();
			if (planScreen != null)
			{
				_planScreenGO = planScreen.gameObject;
				_wasPlanScreenActive = _planScreenGO.activeSelf;
				_planScreenGO.SetActive(false);
			}

			// Disable all KScreen input handlers
			KScreenManager.Instance?.DisableInput(true);
		}

		private void OnDisable()
		{
			using var _ = Profiler.Scope();

			// Re-enable PlayerController
			if (PlayerController.Instance != null)
			{
				PlayerController.Instance.enabled = _wasPlayerControllerEnabled;
			}

			// Re-enable CameraController
			if (CameraController.Instance != null)
			{
				CameraController.Instance.enabled = _wasCameraControllerEnabled;
			}

			// Re-enable ToolMenu
			if (ToolMenu.Instance != null)
			{
				ToolMenu.Instance.gameObject.SetActive(true);
			}

			// Re-enable PlanScreen
			if (_planScreenGO != null)
			{
				_planScreenGO.SetActive(_wasPlanScreenActive);
			}

			// Re-enable KScreen input handlers
			KScreenManager.Instance?.DisableInput(false);
		}

		private void Update()
		{
			using var _ = Profiler.Scope();

			// Consume ESC key to close dialog
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				Close();
			}
		}

		private void Initialize()
		{
			using var _ = Profiler.Scope();

			// Create content container
			var contentGO = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
			contentGO.transform.SetParent(_screenGO.transform, false);

			var contentRT = contentGO.GetComponent<RectTransform>();
			contentRT.anchorMin = new Vector2(0.5f, 0.5f);
			contentRT.anchorMax = new Vector2(0.5f, 0.5f);
			contentRT.pivot = new Vector2(0.5f, 0.5f);
			contentRT.sizeDelta = new Vector2(450, 400);

			var layout = contentGO.GetComponent<VerticalLayoutGroup>();
			layout.padding = new RectOffset(30, 30, 30, 30);
			layout.spacing = 20;
			layout.childAlignment = TextAnchor.UpperCenter;
			layout.childControlHeight = false;
			layout.childControlWidth = true;
			layout.childForceExpandHeight = false;
			layout.childForceExpandWidth = true;

			// Title
			CreateLabel(contentGO.transform, STRINGS.UI.HOSTLOBBYCONFIGSCREEN.HOST_LOBBY_SETTINGS, 28, 45);

			// Divider
			CreateDivider(contentGO.transform);

			// Private/Public Toggle
			CreateVisibilityOption(contentGO.transform);

			// Lobby Size input
			CreateLobbySizeOption(contentGO.transform);

			// Password Input
			CreatePasswordOption(contentGO.transform);

			// Divider
			CreateDivider(contentGO.transform);

			// Buttons
			CreateButtons(contentGO.transform);
		}

		private void CreateLobbySizeOption(Transform parent)
		{
			using var _ = Profiler.Scope();

			var container = new GameObject("LobbySizeOption", typeof(RectTransform), typeof(VerticalLayoutGroup));
			container.transform.SetParent(parent, false);

			var layout = container.GetComponent<VerticalLayoutGroup>();
			layout.spacing = 8;
			layout.childAlignment = TextAnchor.UpperLeft;
			layout.childControlHeight = false;
			layout.childControlWidth = true;

			var rt = container.GetComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0, 75);

			// Label
			CreateLabel(container.transform, STRINGS.UI.HOSTLOBBYCONFIGSCREEN.LOBBY_SIZE, 16, 25);

			// Input field
			_lobbySizeInput = CreateInputField(container.transform, "4", 45, TMP_InputField.ContentType.IntegerNumber);
		}

		private void CreateVisibilityOption(Transform parent)
		{
			using var _ = Profiler.Scope();

			var container = new GameObject("VisibilityOption", typeof(RectTransform), typeof(HorizontalLayoutGroup));
			container.transform.SetParent(parent, false);

			var layout = container.GetComponent<HorizontalLayoutGroup>();
			layout.spacing = 15;
			layout.childAlignment = TextAnchor.MiddleLeft;
			layout.childControlWidth = false;
			layout.childForceExpandWidth = false;

			var rt = container.GetComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0, 40);

			// Label
			var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
			labelGO.transform.SetParent(container.transform, false);
			var labelRT = labelGO.GetComponent<RectTransform>();
			labelRT.sizeDelta = new Vector2(200, 40);

			var labelTmp = labelGO.GetComponent<TextMeshProUGUI>();
			labelTmp.text = STRINGS.UI.HOSTLOBBYCONFIGSCREEN.LOBBY_VISIBILITY;
			labelTmp.fontSize = 18;
			labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
			labelTmp.color = Color.white;

			// Toggle container
			var toggleContainer = new GameObject("ToggleContainer", typeof(RectTransform), typeof(HorizontalLayoutGroup));
			toggleContainer.transform.SetParent(container.transform, false);
			var toggleLayout = toggleContainer.GetComponent<HorizontalLayoutGroup>();
			toggleLayout.spacing = 10;
			toggleLayout.childAlignment = TextAnchor.MiddleLeft;
			toggleLayout.childControlWidth = false;

			var toggleContainerRT = toggleContainer.GetComponent<RectTransform>();
			toggleContainerRT.sizeDelta = new Vector2(180, 40);

			// Create toggle background
			var toggleBG = new GameObject("ToggleBG", typeof(RectTransform), typeof(Image), typeof(Toggle));
			toggleBG.transform.SetParent(toggleContainer.transform, false);
			var toggleBGRT = toggleBG.GetComponent<RectTransform>();
			toggleBGRT.sizeDelta = new Vector2(50, 30);

			var bgImage = toggleBG.GetComponent<Image>();
			bgImage.color = new Color(0.2f, 0.2f, 0.25f);

			// Create checkmark
			var checkmark = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
			checkmark.transform.SetParent(toggleBG.transform, false);
			var checkRT = checkmark.GetComponent<RectTransform>();
			checkRT.anchorMin = Vector2.zero;
			checkRT.anchorMax = Vector2.one;
			checkRT.offsetMin = new Vector2(4, 4);
			checkRT.offsetMax = new Vector2(-4, -4);

			var checkImage = checkmark.GetComponent<Image>();
			checkImage.color = new Color(0.4f, 0.7f, 1f);

			_privateToggle = toggleBG.GetComponent<Toggle>();
			_privateToggle.graphic = checkImage;
			_privateToggle.isOn = Configuration.Instance.Host.Lobby.IsPrivate;
			_privateToggle.onValueChanged.AddListener(OnPrivateToggleChanged);

			// Status label
			var statusGO = new GameObject("StatusLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
			statusGO.transform.SetParent(toggleContainer.transform, false);
			var statusRT = statusGO.GetComponent<RectTransform>();
			statusRT.sizeDelta = new Vector2(100, 30);

			_privateLabel = statusGO.GetComponent<TextMeshProUGUI>();
			_privateLabel.fontSize = 16;
			_privateLabel.alignment = TextAlignmentOptions.MidlineLeft;
			UpdatePrivateLabel();
		}

		private void OnPrivateToggleChanged(bool isPrivate)
		{
			using var _ = Profiler.Scope();

			UpdatePrivateLabel();
		}

		private void UpdatePrivateLabel()
		{
			using var _ = Profiler.Scope();

			if (_privateToggle.isOn)
			{
				_privateLabel.text = STRINGS.UI.HOSTLOBBYCONFIGSCREEN.LOBBY_VISIBILITY_FRIENDSONLY;
				_privateLabel.color = new Color(1f, 0.8f, 0.4f);
			}
			else
			{
				_privateLabel.text = STRINGS.UI.HOSTLOBBYCONFIGSCREEN.LOBBY_VISIBILITY_PUBLIC;
				_privateLabel.color = new Color(0.4f, 1f, 0.6f);
			}
		}

		private void CreatePasswordOption(Transform parent)
		{
			using var _ = Profiler.Scope();

			var container = new GameObject("PasswordOption", typeof(RectTransform), typeof(VerticalLayoutGroup));
			container.transform.SetParent(parent, false);

			var layout = container.GetComponent<VerticalLayoutGroup>();
			layout.spacing = 8;
			layout.childAlignment = TextAnchor.UpperLeft;
			layout.childControlHeight = false;
			layout.childControlWidth = true;

			var rt = container.GetComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0, 75);

			// Label
			CreateLabel(container.transform, STRINGS.UI.HOSTLOBBYCONFIGSCREEN.PASSWORD_TITLE, 16, 25);

			// Input field
			_passwordInput = CreateInputField(container.transform, STRINGS.UI.HOSTLOBBYCONFIGSCREEN.PASSWORD_NOTE, 45, TMP_InputField.ContentType.Password);
		}

		private TMP_InputField CreateInputField(Transform parent, string placeholder, float height, TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard)
		{
			using var _ = Profiler.Scope();

			var inputGO = new GameObject("PasswordInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
			inputGO.transform.SetParent(parent, false);

			var rt = inputGO.GetComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0, height);

			var image = inputGO.GetComponent<Image>();
			image.color = new Color(0.15f, 0.15f, 0.2f);

			// Text area
			var textAreaGO = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
			textAreaGO.transform.SetParent(inputGO.transform, false);
			var textAreaRT = textAreaGO.GetComponent<RectTransform>();
			textAreaRT.anchorMin = Vector2.zero;
			textAreaRT.anchorMax = Vector2.one;
			textAreaRT.offsetMin = new Vector2(10, 5);
			textAreaRT.offsetMax = new Vector2(-10, -5);

			// Placeholder
			var placeholderGO = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
			placeholderGO.transform.SetParent(textAreaGO.transform, false);
			var placeholderRT = placeholderGO.GetComponent<RectTransform>();
			placeholderRT.anchorMin = Vector2.zero;
			placeholderRT.anchorMax = Vector2.one;
			placeholderRT.sizeDelta = Vector2.zero;

			var placeholderTMP = placeholderGO.GetComponent<TextMeshProUGUI>();
			placeholderTMP.text = placeholder;
			placeholderTMP.fontSize = 16;
			placeholderTMP.color = new Color(0.5f, 0.5f, 0.5f);
			placeholderTMP.alignment = TextAlignmentOptions.MidlineLeft;

			// Text
			var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
			textGO.transform.SetParent(textAreaGO.transform, false);
			var textRT = textGO.GetComponent<RectTransform>();
			textRT.anchorMin = Vector2.zero;
			textRT.anchorMax = Vector2.one;
			textRT.sizeDelta = Vector2.zero;

			var textTMP = textGO.GetComponent<TextMeshProUGUI>();
			textTMP.fontSize = 18;
			textTMP.color = Color.white;
			textTMP.alignment = TextAlignmentOptions.MidlineLeft;

			var inputField = inputGO.GetComponent<TMP_InputField>();
			inputField.textViewport = textAreaRT;
			inputField.textComponent = textTMP;
			inputField.placeholder = placeholderTMP;
			inputField.text = "";
			inputField.contentType = contentType;

			return inputField;
		}

		private void CreateButtons(Transform parent)
		{
			using var _ = Profiler.Scope();

			var container = new GameObject("ButtonContainer", typeof(RectTransform), typeof(HorizontalLayoutGroup));
			container.transform.SetParent(parent, false);

			var layout = container.GetComponent<HorizontalLayoutGroup>();
			layout.spacing = 25;
			layout.childAlignment = TextAnchor.MiddleCenter;
			layout.childControlWidth = false;

			var rt = container.GetComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0, 55);

			// Continue button
			CreateButton(container.transform, STRINGS.UI.HOSTLOBBYCONFIGSCREEN.CONTINUE, OnContinueClicked, 150, 50);

			// Cancel button
			CreateButton(container.transform, STRINGS.UI.HOSTLOBBYCONFIGSCREEN.CANCEL, () => Close(), 120, 50);
		}

		private void OnContinueClicked()
		{
			using var _ = Profiler.Scope();

			// Save settings to config
			Configuration.Instance.Host.Lobby.IsPrivate = _privateToggle.isOn;

			string input = _lobbySizeInput.text ?? "";
			if (!string.IsNullOrEmpty(input))
			{
				int max_size = Mathf.Clamp(int.Parse(input), NetworkConfig.LOBBY_SIZE_MIN, NetworkConfig.LOBBY_SIZE_MAX);
				Configuration.Instance.Host.MaxLobbySize = max_size;
			}
			else
			{
				Configuration.Instance.Host.MaxLobbySize = NetworkConfig.LOBBY_SIZE_DEFAULT;
			}

			string password = _passwordInput?.text ?? "";
			if (!string.IsNullOrEmpty(password))
			{
				Configuration.Instance.Host.Lobby.RequirePassword = true;
				Configuration.Instance.Host.Lobby.PasswordHash = PasswordHelper.HashPassword(password);
			}
			else
			{
				Configuration.Instance.Host.Lobby.RequirePassword = false;
				Configuration.Instance.Host.Lobby.PasswordHash = "";
			}

			Configuration.Instance.Save();

			Close();

			// If callback provided, use it (in-game hosting)
			if (_onContinue != null)
			{
				var callback = _onContinue;
				_onContinue = null;
				callback.Invoke();
			}
			else
			{
				// Main menu flow - set flag and open Load Game
				MultiplayerSession.ShouldHostAfterLoad = true;

				var mainMenu = FindFirstObjectByType<MainMenu>();
				if (mainMenu != null)
				{
					if (mainMenu.saveFileEntries.Count > 0)
					{
						DebugConsole.Log($"[HostLobbyConfigScreen] Found {mainMenu.saveFileEntries.Count} saves. Opening load sequence");
						mainMenu.LoadGame();
					}
					else
					{
						DebugConsole.Log("$[HostLobbyConfigScreen] No saves found! Running new game sequence.");
						mainMenu.NewGame();
					}
				}
			}
		}

		private void CreateLabel(Transform parent, string text, int fontSize, float height)
		{
			using var _ = Profiler.Scope();

			var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
			labelGO.transform.SetParent(parent, false);

			var tmp = labelGO.GetComponent<TextMeshProUGUI>();
			tmp.text = text;
			tmp.fontSize = fontSize;
			tmp.alignment = TextAlignmentOptions.Center;
			tmp.color = Color.white;
			tmp.fontStyle = FontStyles.Bold;

			var rt = labelGO.GetComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0, height);
		}

		private void CreateDivider(Transform parent)
		{
			using var _ = Profiler.Scope();

			var dividerGO = new GameObject("Divider", typeof(RectTransform), typeof(Image));
			dividerGO.transform.SetParent(parent, false);

			var rt = dividerGO.GetComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0, 2);

			var image = dividerGO.GetComponent<Image>();
			image.color = new Color(0.3f, 0.3f, 0.4f, 0.5f);
		}

		private void CreateButton(Transform parent, string text, System.Action onClick, float width, float height)
		{
			using var _ = Profiler.Scope();

			var mainMenu = FindFirstObjectByType<MainMenu>();
			var templateButton = mainMenu?.Button_NewGame;

			if (templateButton != null)
			{
				var buttonGO = Instantiate(templateButton.gameObject, parent);
				buttonGO.name = $"Button_{text}";

				var rt = buttonGO.GetComponent<RectTransform>();
				rt.sizeDelta = new Vector2(width, height);

				var btn = buttonGO.GetComponent<KButton>();
				btn.ClearOnClick();
				btn.onClick += onClick;

				var locTexts = buttonGO.GetComponentsInChildren<LocText>(true);
				if (locTexts.Length > 0)
					locTexts[0].SetText(text);
				if (locTexts.Length > 1)
					locTexts[1].SetText("");
			}
			else
			{
				// Fallback button
				var buttonGO = new GameObject($"Button_{text}", typeof(RectTransform), typeof(Image), typeof(Button));
				buttonGO.transform.SetParent(parent, false);

				var rt = buttonGO.GetComponent<RectTransform>();
				rt.sizeDelta = new Vector2(width, height);

				var image = buttonGO.GetComponent<Image>();
				image.color = new Color(0.2f, 0.35f, 0.5f);

				var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
				labelGO.transform.SetParent(buttonGO.transform, false);
				var labelRT = labelGO.GetComponent<RectTransform>();
				labelRT.anchorMin = Vector2.zero;
				labelRT.anchorMax = Vector2.one;
				labelRT.sizeDelta = Vector2.zero;

				var tmp = labelGO.GetComponent<TextMeshProUGUI>();
				tmp.text = text;
				tmp.fontSize = 18;
				tmp.alignment = TextAlignmentOptions.Center;
				tmp.color = Color.white;

				var button = buttonGO.GetComponent<Button>();
				button.onClick.AddListener(() => onClick?.Invoke());
			}
		}
	}
}

