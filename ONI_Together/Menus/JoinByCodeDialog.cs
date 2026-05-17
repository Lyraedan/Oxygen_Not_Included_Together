using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Transport.Steamworks;
using Shared.Profiling;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ONI_Together.Menus
{
    /// <summary>
    /// Dialog for joining a lobby by entering a code.
    /// Flow: Enter code → Try Join → If password needed, show password prompt → Validate → Enter
    /// </summary>
    public class JoinByCodeDialog : MonoBehaviour
    {
        private static JoinByCodeDialog _instance;
        private static GameObject _dialogGO;

        private TMP_InputField _codeInput;
        private TMP_InputField _passwordInput;
        private GameObject _passwordContainer;
        private TextMeshProUGUI _errorText;
        private TextMeshProUGUI _statusText;

        private ulong _pendingLobbyId = Utils.NilUlong();
        private bool _awaitingPasswordRetry = false;

        public static void Show(Transform parent)
        {
            using var _ = Profiler.Scope();

            if (_instance != null)
            {
                DebugConsole.Log("[JoinByCodeDialog] Dialog already open.");
                return;
            }

            _dialogGO = CreateDialog(parent);
            _instance = _dialogGO.AddComponent<JoinByCodeDialog>();
            _instance.Initialize();
        }

        public static void Close()
        {
            using var _ = Profiler.Scope();

            if (_dialogGO != null)
            {
                Destroy(_dialogGO);
                _dialogGO = null;
                _instance = null;
            }
        }

        private static GameObject CreateDialog(Transform parent)
        {
            using var _ = Profiler.Scope();

            GameObject dialog = new GameObject("JoinByCodeDialog", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            dialog.transform.SetParent(parent, false);

            var rt = dialog.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(400, 280);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            var image = dialog.GetComponent<Image>();
            image.color = new Color(0.1f, 0.1f, 0.15f, 0.98f);

            var canvasGroup = dialog.GetComponent<CanvasGroup>();
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;

            var layout = dialog.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(25, 25, 25, 25);
            layout.spacing = 12;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = false;
            layout.childControlWidth = true;

            return dialog;
        }

        private void Initialize()
        {
            using var _ = Profiler.Scope();

            // Title
            CreateLabel(_dialogGO.transform, STRINGS.UI.JOINBYDIALOGMENU.JOIN_BY_CODE, 22, FontStyles.Bold, 30);

            // Code input section
            CreateLabel(_dialogGO.transform, STRINGS.UI.JOINBYDIALOGMENU.ENTER_LOBBY_CODE, 14, FontStyles.Normal, 20);
            _codeInput = CreateInputField(_dialogGO.transform, STRINGS.UI.JOINBYDIALOGMENU.DEFAULT_CODE, 40);
            _codeInput.characterLimit = 16;

            // Password section (initially hidden)
            _passwordContainer = new GameObject("PasswordContainer", typeof(RectTransform));
            _passwordContainer.transform.SetParent(_dialogGO.transform, false);
            var passRT = _passwordContainer.GetComponent<RectTransform>();
            passRT.sizeDelta = new Vector2(0, 70);

            var passLayout = _passwordContainer.AddComponent<VerticalLayoutGroup>();
            passLayout.spacing = 5;
            passLayout.childControlHeight = false;
            passLayout.childControlWidth = true;

            CreateLabel(_passwordContainer.transform, STRINGS.UI.JOINBYDIALOGMENU.PASSWORD_REQUIRED, 14, FontStyles.Normal, 20);
            _passwordInput = CreateInputField(_passwordContainer.transform, STRINGS.UI.JOINBYDIALOGMENU.ENTER_PASSWORD, 40);
            _passwordInput.contentType = TMP_InputField.ContentType.Password;

            _passwordContainer.SetActive(false); // Hidden initially

            // Status/Error text
            var statusGO = CreateLabel(_dialogGO.transform, "", 14, FontStyles.Normal, 25);
            _statusText = statusGO.GetComponent<TextMeshProUGUI>();
            _statusText.color = new Color(0.7f, 0.7f, 0.7f);

            var errorGO = CreateLabel(_dialogGO.transform, "", 14, FontStyles.Normal, 20);
            _errorText = errorGO.GetComponent<TextMeshProUGUI>();
            _errorText.color = new Color(1f, 0.4f, 0.4f);

            // Button container
            var buttonContainer = new GameObject("ButtonContainer", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            buttonContainer.transform.SetParent(_dialogGO.transform, false);
            var buttonLayout = buttonContainer.GetComponent<HorizontalLayoutGroup>();
            buttonLayout.spacing = 20;
            buttonLayout.childAlignment = TextAnchor.MiddleCenter;
            buttonLayout.childControlWidth = false;

            var buttonContainerRT = buttonContainer.GetComponent<RectTransform>();
            buttonContainerRT.sizeDelta = new Vector2(0, 50);

            CreateButton(buttonContainer.transform, STRINGS.UI.JOINBYDIALOGMENU.JOIN, OnJoinClicked, 120, 42);
            CreateButton(buttonContainer.transform, STRINGS.UI.JOINBYDIALOGMENU.CANCEL, () => Close(), 100, 42);
        }

        private void OnJoinClicked()
        {
            using var _ = Profiler.Scope();

            _errorText.text = "";

            // If we're in password retry mode, validate the password
            if (_awaitingPasswordRetry)
            {
                ValidatePassword();
                return;
            }

            // First step: Validate and parse code
            string code = LobbyCodeHelper.CleanCode(_codeInput.text);

            if (string.IsNullOrEmpty(code))
            {
                _errorText.text = STRINGS.UI.JOINBYDIALOGMENU.ERR_ENTER_CODE;
                return;
            }

            if (!LobbyCodeHelper.IsValidCodeFormat(code))
            {
                _errorText.text = STRINGS.UI.JOINBYDIALOGMENU.ERR_INVALID_CODE;
                return;
            }

            if (!LobbyCodeHelper.TryParseCode(code, out ulong lobbyId))
            {
                _errorText.text = STRINGS.UI.JOINBYDIALOGMENU.ERR_PARSE_CODE_FAILED;
                return;
            }

            _pendingLobbyId = lobbyId;
            _statusText.text = STRINGS.UI.JOINBYDIALOGMENU.CHECKING_LOBBY;

            // We need to join the lobby to get its metadata (including password status)
            // But first, let's check if we can get the data by requesting lobby data
            SteamMatchmaking.RequestLobbyData(lobbyId.AsCSteamID());

            // Wait a moment for data to arrive, then check password
            StartCoroutine(CheckLobbyPasswordAfterDelay(lobbyId));
        }

        private System.Collections.IEnumerator CheckLobbyPasswordAfterDelay(ulong lobbyId)
        {
            using var _ = Profiler.Scope();

            yield return new WaitForSeconds(0.5f);

            // Check if lobby requires password
            string hasPassword = SteamMatchmaking.GetLobbyData(lobbyId.AsCSteamID(), "has_password");

            if (hasPassword == "1")
            {
                // Show password input
                _statusText.text = STRINGS.UI.JOINBYDIALOGMENU.LOBBY_REQUIRES_PASSWORD;
                _passwordContainer.SetActive(true);
                _awaitingPasswordRetry = true;

                // Expand dialog
                var rt = _dialogGO.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(400, 350);
            }
            else
            {
                // No password needed, join directly
                JoinLobbyDirectly(lobbyId, null);
            }
        }

        private void ValidatePassword()
        {
            using var _ = Profiler.Scope();

            string password = _passwordInput.text;

            if (string.IsNullOrEmpty(password))
            {
                _errorText.text = STRINGS.UI.JOINBYDIALOGMENU.VALIDATE_ENTER_PASSWORD;
                return;
            }

            // Check password against stored hash
            string storedHash = SteamMatchmaking.GetLobbyData(_pendingLobbyId.AsCSteamID(), "password_hash");
            if (!string.IsNullOrEmpty(storedHash))
            {
                if (!PasswordHelper.VerifyPassword(password, storedHash))
                {
                    _errorText.text = STRINGS.UI.JOINBYDIALOGMENU.VALIDATE_ERR_INCORRECT_PASSWORD;
                    _passwordInput.text = "";
                    return;
                }
            }

            // Password correct, join
            JoinLobbyDirectly(_pendingLobbyId, password);
        }

        private void JoinLobbyDirectly(ulong lobbyId, string password)
        {
            using var _ = Profiler.Scope();

            _statusText.text = STRINGS.UI.JOINBYDIALOGMENU.JOINING;
            _errorText.text = "";

            SteamLobby.JoinLobby(lobbyId.AsCSteamID(), (joinedId) =>
            {
                DebugConsole.Log($"[JoinByCodeDialog] Successfully joined lobby: {joinedId}");
                Close();
            }, password);
        }

        private GameObject CreateLabel(Transform parent, string text, int fontSize, FontStyles style, float height)
        {
            using var _ = Profiler.Scope();

            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGO.transform.SetParent(parent, false);

            var tmp = labelGO.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            var rt = labelGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, height);

            return labelGO;
        }

        private TMP_InputField CreateInputField(Transform parent, string placeholder, float height)
        {
            using var _ = Profiler.Scope();

            var inputGO = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            inputGO.transform.SetParent(parent, false);

            var rt = inputGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, height);

            var image = inputGO.GetComponent<Image>();
            image.color = new Color(0.15f, 0.15f, 0.2f);

            var textAreaGO = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            textAreaGO.transform.SetParent(inputGO.transform, false);
            var textAreaRT = textAreaGO.GetComponent<RectTransform>();
            textAreaRT.anchorMin = Vector2.zero;
            textAreaRT.anchorMax = Vector2.one;
            textAreaRT.offsetMin = new Vector2(10, 5);
            textAreaRT.offsetMax = new Vector2(-10, -5);

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
            placeholderTMP.alignment = TextAlignmentOptions.Center;

            var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGO.transform.SetParent(textAreaGO.transform, false);
            var textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = Vector2.zero;

            var textTMP = textGO.GetComponent<TextMeshProUGUI>();
            textTMP.fontSize = 18;
            textTMP.color = Color.white;
            textTMP.alignment = TextAlignmentOptions.Center;

            var inputField = inputGO.GetComponent<TMP_InputField>();
            inputField.textViewport = textAreaRT;
            inputField.textComponent = textTMP;
            inputField.placeholder = placeholderTMP;
            inputField.text = "";

            return inputField;
        }

        private void CreateButton(Transform parent, string text, System.Action onClick, float width, float height)
        {
            using var _ = Profiler.Scope();

            var buttonGO = new GameObject($"Button_{text}", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGO.transform.SetParent(parent, false);

            var rt = buttonGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, height);

            var image = buttonGO.GetComponent<Image>();
            image.color = new Color(0.25f, 0.38f, 0.52f);

            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGO.transform.SetParent(buttonGO.transform, false);
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.sizeDelta = Vector2.zero;

            var tmp = labelGO.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 16;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            var button = buttonGO.GetComponent<Button>();
            button.onClick.AddListener(() => onClick?.Invoke());
        }
    }
}
