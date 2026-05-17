using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Transport.Steamworks;
using Steamworks;
using System.Collections.Generic;
using System.Linq;
using Shared.Profiling;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ONI_Together.Menus
{
    /// <summary>
    /// Lobby browser screen for browsing and joining public lobbies.
    /// </summary>
    public class LobbyBrowserScreen : MonoBehaviour
    {
        private static LobbyBrowserScreen _instance;
        private static GameObject _screenGO;

        private Transform _lobbyListContainer;
        private TextMeshProUGUI _statusText;

        private List<LobbyListEntry> _allLobbies = new List<LobbyListEntry>();
        private List<LobbyListEntry> _filteredLobbies = new List<LobbyListEntry>();
        private List<GameObject> _lobbyRowObjects = new List<GameObject>();

        private SortMode _sortMode = SortMode.ByPing;

        private enum SortMode
        {
            ByPing,
            ByPlayers,
            ByName
        }

        public static void Show(Transform parent)
        {
            using var _ = Profiler.Scope();

            if (_instance != null)
            {
                DebugConsole.Log("[LobbyBrowserScreen] Screen already open.");
                return;
            }

            _screenGO = CreateScreen(parent);
            _instance = _screenGO.AddComponent<LobbyBrowserScreen>();
            _instance.Initialize();
            _instance.RefreshLobbyList();
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

            GameObject screen = new GameObject("LobbyBrowserScreen", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            screen.transform.SetParent(parent, false);

            var rt = screen.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(80, 40);
            rt.offsetMax = new Vector2(-80, -40);

            var image = screen.GetComponent<Image>();
            image.color = new Color(0.06f, 0.06f, 0.1f, 0.98f);

            var canvasGroup = screen.GetComponent<CanvasGroup>();
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;

            return screen;
        }

        private void Initialize()
        {
            using var _ = Profiler.Scope();

            // Initialize Steam relay network for ping estimation
            try { Steamworks.SteamNetworkingUtils.InitRelayNetworkAccess(); }
            catch { /* Ignore if not available */ }

            // Main layout
            var mainLayout = _screenGO.AddComponent<VerticalLayoutGroup>();
            mainLayout.padding = new RectOffset(25, 25, 25, 25);
            mainLayout.spacing = 15;
            mainLayout.childAlignment = TextAnchor.UpperCenter;
            mainLayout.childControlHeight = false;
            mainLayout.childControlWidth = true;
            mainLayout.childForceExpandHeight = false;
            mainLayout.childForceExpandWidth = true;

            // Header
            CreateHeader();

            // Column headers
            CreateColumnHeaders();

            // Scrollable lobby list
            CreateLobbyList();

            // Status text
            _statusText = CreateLabel(_screenGO.transform, STRINGS.UI.SERVERBROWSER.LOADING_LOBBIES, 14, 25);
            _statusText.color = new Color(0.7f, 0.7f, 0.7f);

            // Bottom buttons
            CreateBottomButtons();
        }

        private void CreateHeader()
        {
            using var _ = Profiler.Scope();

            var headerGO = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            headerGO.transform.SetParent(_screenGO.transform, false);

            var layout = headerGO.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 20;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;

            var rt = headerGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 50);

            // Title
            var titleGO = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGO.transform.SetParent(headerGO.transform, false);
            var titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.sizeDelta = new Vector2(250, 50);

            var titleTmp = titleGO.GetComponent<TextMeshProUGUI>();
            titleTmp.text = STRINGS.UI.SERVERBROWSER.TITLE;
            titleTmp.fontSize = 26;
            titleTmp.alignment = TextAlignmentOptions.Center;
            titleTmp.color = Color.white;
            titleTmp.fontStyle = FontStyles.Bold;

            // Search field
            CreateSearchField(headerGO.transform);

            // Refresh button
            CreateButton(headerGO.transform, STRINGS.UI.SERVERBROWSER.REFRESH, () => RefreshLobbyList(), 100, 40);
        }

        private TMP_InputField _searchInput;
        private string _searchQuery = "";

        private void CreateSearchField(Transform parent)
        {
            using var _ = Profiler.Scope();

            var inputGO = new GameObject("SearchInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            inputGO.transform.SetParent(parent, false);

            var rt = inputGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(200, 36);

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
            placeholderTMP.text = string.Format(STRINGS.UI.SERVERBROWSER.SEARCH, "🔍");
            placeholderTMP.fontSize = 14;
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
            textTMP.fontSize = 14;
            textTMP.color = Color.white;
            textTMP.alignment = TextAlignmentOptions.MidlineLeft;

            _searchInput = inputGO.GetComponent<TMP_InputField>();
            _searchInput.textViewport = textAreaRT;
            _searchInput.textComponent = textTMP;
            _searchInput.placeholder = placeholderTMP;
            _searchInput.onValueChanged.AddListener(OnSearchChanged);
        }

        private void OnSearchChanged(string query)
        {
            using var _ = Profiler.Scope();

            _searchQuery = query?.ToLower() ?? "";
            ApplyFiltersAndSort();
            int lobbyCount;
            PopulateLobbyList(out lobbyCount);
        }

        private void CreateColumnHeaders()
        {
            using var _ = Profiler.Scope();

            var headerContainer = new GameObject("ColumnHeaders", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(Image));
            headerContainer.transform.SetParent(_screenGO.transform, false);

            var image = headerContainer.GetComponent<Image>();
            image.color = new Color(0.12f, 0.12f, 0.18f);

            var layout = headerContainer.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(15, 15, 8, 8);
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;

            var rt = headerContainer.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 40);

            // Column headers - Mixed flexible and fixed widths
            CreateHeaderCell(headerContainer.transform, STRINGS.UI.SERVERBROWSER.HEADERS.COLONY, 0, 1f);      // Flexible
            CreateHeaderCell(headerContainer.transform, STRINGS.UI.SERVERBROWSER.HEADERS.HOST, 0, 0.8f);      // Flexible
            CreateHeaderCell(headerContainer.transform, STRINGS.UI.SERVERBROWSER.HEADERS.PLAYERS, 65, 0);     // Fixed
            CreateHeaderCell(headerContainer.transform, STRINGS.UI.SERVERBROWSER.HEADERS.CYCLE, 55, 0);       // Fixed
            CreateHeaderCell(headerContainer.transform, STRINGS.UI.SERVERBROWSER.HEADERS.DUPES, 55, 0);       // Fixed
            CreateHeaderCell(headerContainer.transform, STRINGS.UI.SERVERBROWSER.HEADERS.PING, 55, 0);        // Fixed
            CreateHeaderCell(headerContainer.transform, "", 70, 0);            // Join button
        }

        private void CreateHeaderCell(Transform parent, string text, float minWidth, float flexibleWidth)
        {
            using var _ = Profiler.Scope();

            var cellGO = new GameObject("HeaderCell", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            cellGO.transform.SetParent(parent, false);

            var layoutElem = cellGO.GetComponent<LayoutElement>();
            if (flexibleWidth > 0)
            {
                layoutElem.flexibleWidth = flexibleWidth;
                layoutElem.minWidth = 80;
            }
            else
            {
                layoutElem.preferredWidth = minWidth;
                layoutElem.minWidth = minWidth;
            }

            var rt = cellGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(minWidth > 0 ? minWidth : 100, 30);

            var tmp = cellGO.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 14;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = new Color(0.8f, 0.8f, 0.9f);
        }

        private void CreateLobbyList()
        {
            using var _ = Profiler.Scope();

            // Scroll view container
            var scrollViewGO = new GameObject("ScrollView", typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(Mask));
            scrollViewGO.transform.SetParent(_screenGO.transform, false);

            var scrollRT = scrollViewGO.GetComponent<RectTransform>();
            scrollRT.sizeDelta = new Vector2(0, 600); //320

            var scrollImage = scrollViewGO.GetComponent<Image>();
            scrollImage.color = new Color(0.08f, 0.08f, 0.12f);

            var scrollRect = scrollViewGO.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            // Content container
            var contentGO = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGO.transform.SetParent(scrollViewGO.transform, false);

            var contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.sizeDelta = new Vector2(0, 0);

            var contentLayout = contentGO.GetComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(5, 5, 5, 5);
            contentLayout.spacing = 4;
            contentLayout.childControlHeight = false;
            contentLayout.childControlWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childForceExpandWidth = true;

            var contentFitter = contentGO.GetComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.content = contentRT;
            _lobbyListContainer = contentGO.transform;
        }

        private void CreateBottomButtons()
        {
            using var _ = Profiler.Scope();

            var buttonContainer = new GameObject("ButtonContainer", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            buttonContainer.transform.SetParent(_screenGO.transform, false);

            var layout = buttonContainer.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 25;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;

            var rt = buttonContainer.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 55);

            // Join by Code button
            CreateButton(buttonContainer.transform, STRINGS.UI.SERVERBROWSER.JOIN_BY_CODE, () =>
            {
                var parent = _screenGO.transform.parent;
                Close();
                JoinByCodeDialog.Show(parent);
            }, 140, 45);

            // Back button
            CreateButton(buttonContainer.transform, STRINGS.UI.SERVERBROWSER.BACK, () => Close(), 100, 45);
        }

        private void RefreshLobbyList()
        {
            using var _ = Profiler.Scope();

            _statusText.text = STRINGS.UI.SERVERBROWSER.LOADING_LOBBIES;
            ClearLobbyRows();

            SteamLobby.RequestLobbyList(OnLobbyListReceived);
        }

        private void OnLobbyListReceived(List<LobbyListEntry> lobbies)
        {
            using var _ = Profiler.Scope();

            _allLobbies = lobbies ?? new List<LobbyListEntry>();

            ApplyFiltersAndSort();
            int lobbyCount = 0;
            PopulateLobbyList(out lobbyCount);

            if (lobbyCount == 0)
            {
                _statusText.text = STRINGS.UI.SERVERBROWSER.NO_PUBLIC_LOBBIES_FOUND;
            }
            else
            {
                _statusText.text = string.Format(STRINGS.UI.SERVERBROWSER.FOUND_X_LOBBIES, lobbyCount);
            }
        }

        private void ApplyFiltersAndSort()
        {
            using var _ = Profiler.Scope();

            // First, filter by search query
            var filtered = _allLobbies.AsEnumerable();

            if (!string.IsNullOrEmpty(_searchQuery))
            {
                filtered = filtered.Where(l =>
                    (l.ColonyDisplay?.ToLower().Contains(_searchQuery) ?? false) ||
                    (l.HostName?.ToLower().Contains(_searchQuery) ?? false));
            }

            // Then sort
            switch (_sortMode)
            {
                case SortMode.ByPing:
                    // Friends first, then by player count
                    _filteredLobbies = filtered
                        .OrderByDescending(l => l.IsFriend)
                        .ThenByDescending(l => l.PlayerCount)
                        .ToList();
                    break;
                case SortMode.ByPlayers:
                    _filteredLobbies = filtered
                        .OrderByDescending(l => l.IsFriend)
                        .ThenByDescending(l => l.PlayerCount)
                        .ToList();
                    break;
                case SortMode.ByName:
                    _filteredLobbies = filtered
                        .OrderByDescending(l => l.IsFriend)
                        .ThenBy(l => l.ColonyName)
                        .ToList();
                    break;
                default:
                    _filteredLobbies = filtered.ToList();
                    break;
            }
        }

        private void ClearLobbyRows()
        {
            using var _ = Profiler.Scope();

            foreach (var row in _lobbyRowObjects)
            {
                if (row != null)
                    Destroy(row);
            }
            _lobbyRowObjects.Clear();
        }

        private void PopulateLobbyList(out int lobbyCount)
        {
            using var _ = Profiler.Scope();

            ClearLobbyRows();

            foreach (var lobby in _filteredLobbies)
            {
                // Its private AND we are not friends with the host then HIDE from the list
                if (lobby.IsPrivate && !SteamFriends.HasFriend(lobby.HostSteamId.AsCSteamID(), EFriendFlags.k_EFriendFlagImmediate))
                    continue;

                var rowGO = CreateLobbyRow(lobby);
                _lobbyRowObjects.Add(rowGO);
            }
            lobbyCount = _lobbyRowObjects.Count;
        }

        private GameObject CreateLobbyRow(LobbyListEntry lobby)
        {
            using var _ = Profiler.Scope();

            var rowGO = new GameObject("LobbyRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(Image));
            rowGO.transform.SetParent(_lobbyListContainer, false);

            var image = rowGO.GetComponent<Image>();
            image.color = new Color(0.1f, 0.1f, 0.14f);

            var layout = rowGO.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(15, 15, 8, 8);
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;

            var rt = rowGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 42);

            // Cells with matching flexible/fixed widths as headers
            string nameDisplay = lobby.HasPassword ? "🔒 " + lobby.ColonyDisplay : lobby.ColonyDisplay;
            CreateCell(rowGO.transform, nameDisplay, 0, 1f);           // Flexible
            CreateCell(rowGO.transform, lobby.HostDisplayWithBadge, 0, 0.8f);  // Flexible
            CreateCell(rowGO.transform, lobby.PlayerCountDisplay, 65, 0);      // Fixed
            CreateCell(rowGO.transform, lobby.CycleDisplay, 55, 0);            // Fixed
            CreateCell(rowGO.transform, lobby.DuplicantDisplay, 55, 0);        // Fixed
            CreateCell(rowGO.transform, lobby.PingDisplay, 55, 0);             // Fixed

            // Join button
            bool join_interactable = !lobby.IsPrivate || SteamFriends.HasFriend(lobby.HostSteamId.AsCSteamID(), EFriendFlags.k_EFriendFlagImmediate);
            string label = join_interactable ? STRINGS.UI.SERVERBROWSER.JOIN_BUTTON : STRINGS.UI.SERVERBROWSER.FRIEND_ONLY;
            CreateButton(rowGO.transform, label, () => JoinLobby(lobby), 70, 30, join_interactable);

            return rowGO;
        }

        private void CreateCell(Transform parent, string text, float minWidth, float flexibleWidth)
        {
            using var _ = Profiler.Scope();

            var cellGO = new GameObject("Cell", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            cellGO.transform.SetParent(parent, false);

            var layoutElem = cellGO.GetComponent<LayoutElement>();
            if (flexibleWidth > 0)
            {
                layoutElem.flexibleWidth = flexibleWidth;
                layoutElem.minWidth = 80;
            }
            else
            {
                layoutElem.preferredWidth = minWidth;
                layoutElem.minWidth = minWidth;
            }

            var rt = cellGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(minWidth > 0 ? minWidth : 100, 30);

            var tmp = cellGO.GetComponent<TextMeshProUGUI>();
            tmp.text = text ?? "";
            tmp.fontSize = 13;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = Color.white;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
        }

        private void JoinLobby(LobbyListEntry lobby)
        {
            using var _ = Profiler.Scope();

            DebugConsole.Log($"[LobbyBrowser] Joining lobby: {lobby.ColonyDisplay}");

            if (lobby.HasPassword)
            {
                // Show password dialog
                var parent = _screenGO.transform.parent;
                ShowPasswordDialog(parent, lobby);
            }
            else
            {
                // Direct join
                SteamLobby.JoinLobby(lobby.LobbyId.AsCSteamID(), (lobbyId) =>
                {
                    DebugConsole.Log($"[LobbyBrowser] Successfully joined lobby: {lobbyId}");
                    Close();
                });
            }
        }

        public static void ShowPasswordDialog(Transform parent, LobbyListEntry lobby)
        {
            using var _ = Profiler.Scope();

            ShowPasswordDialog(parent, lobby.LobbyId);
        }

        public static void ShowPasswordDialog(Transform parent, ulong lobbyID)
        {
            using var _ = Profiler.Scope();

            // Create password dialog
            var dialogGO = new GameObject("PasswordDialog", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            dialogGO.transform.SetParent(parent, false);

            var dialogRT = dialogGO.GetComponent<RectTransform>();
            dialogRT.anchorMin = new Vector2(0.5f, 0.5f);
            dialogRT.anchorMax = new Vector2(0.5f, 0.5f);
            dialogRT.pivot = new Vector2(0.5f, 0.5f);
            dialogRT.sizeDelta = new Vector2(350, 200);

            var dialogImage = dialogGO.GetComponent<Image>();
            dialogImage.color = new Color(0.1f, 0.1f, 0.15f, 0.98f);

            var dialogCG = dialogGO.GetComponent<CanvasGroup>();
            dialogCG.interactable = true;
            dialogCG.blocksRaycasts = true;

            // Layout
            var layout = dialogGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.spacing = 15;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = false;
            layout.childControlWidth = true;

            // Title
            var titleGO = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGO.transform.SetParent(dialogGO.transform, false);
            var titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.sizeDelta = new Vector2(0, 30);
            var titleTMP = titleGO.GetComponent<TextMeshProUGUI>();
            titleTMP.text = STRINGS.UI.SERVERBROWSER.PASSWORD_REQUIRED;
            titleTMP.fontSize = 20;
            titleTMP.alignment = TextAlignmentOptions.Center;
            titleTMP.color = Color.white;

            // Password input placeholder
            var inputGO = new GameObject("PasswordInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            inputGO.transform.SetParent(dialogGO.transform, false);
            var inputRT = inputGO.GetComponent<RectTransform>();
            inputRT.sizeDelta = new Vector2(0, 40);
            var inputImage = inputGO.GetComponent<Image>();
            inputImage.color = new Color(0.15f, 0.15f, 0.2f);

            // Text area
            var textAreaGO = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            textAreaGO.transform.SetParent(inputGO.transform, false);
            var textAreaRT = textAreaGO.GetComponent<RectTransform>();
            textAreaRT.anchorMin = Vector2.zero;
            textAreaRT.anchorMax = Vector2.one;
            textAreaRT.offsetMin = new Vector2(10, 5);
            textAreaRT.offsetMax = new Vector2(-10, -5);

            // Text
            var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGO.transform.SetParent(textAreaGO.transform, false);
            var textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = Vector2.zero;
            var textTMP = textGO.GetComponent<TextMeshProUGUI>();
            textTMP.fontSize = 16;
            textTMP.color = Color.white;
            textTMP.alignment = TextAlignmentOptions.MidlineLeft;

            var inputField = inputGO.GetComponent<TMP_InputField>();
            inputField.textViewport = textAreaRT;
            inputField.textComponent = textTMP;
            inputField.contentType = TMP_InputField.ContentType.Password;

            // Error text
            var errorGO = new GameObject("Error", typeof(RectTransform), typeof(TextMeshProUGUI));
            errorGO.transform.SetParent(dialogGO.transform, false);
            var errorRT = errorGO.GetComponent<RectTransform>();
            errorRT.sizeDelta = new Vector2(0, 20);
            var errorTMP = errorGO.GetComponent<TextMeshProUGUI>();
            errorTMP.text = "";
            errorTMP.fontSize = 12;
            errorTMP.alignment = TextAlignmentOptions.Center;
            errorTMP.color = new Color(1f, 0.3f, 0.3f);

            // Buttons
            var btnContainer = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            btnContainer.transform.SetParent(dialogGO.transform, false);
            var btnRT = btnContainer.GetComponent<RectTransform>();
            btnRT.sizeDelta = new Vector2(0, 40);
            var btnLayout = btnContainer.GetComponent<HorizontalLayoutGroup>();
            btnLayout.spacing = 20;
            btnLayout.childAlignment = TextAnchor.MiddleCenter;
            btnLayout.childControlWidth = false;

            // Join button
            CreateButton(btnContainer.transform, STRINGS.UI.SERVERBROWSER.JOIN_BUTTON, () =>
            {
                string password = inputField.text;
                if (SteamLobby.ValidateLobbyPassword(lobbyID, password))
                {
                    Destroy(dialogGO);
                    SteamLobby.JoinLobby(lobbyID.AsCSteamID(), (lobbyId) =>
                    {
                        DebugConsole.Log($"[LobbyBrowser] Successfully joined lobby: {lobbyId}");
                        Close();
                    });
                }
                else
                {
                    errorTMP.text = STRINGS.UI.SERVERBROWSER.PASSWORD_INCORRECT;
                }
            }, 100, 35);

            // Cancel button
            CreateButton(btnContainer.transform, STRINGS.UI.SERVERBROWSER.CANCEL, () =>
            {
                Destroy(dialogGO);
            }, 100, 35);
        }

        private TextMeshProUGUI CreateLabel(Transform parent, string text, int fontSize, float height)
        {
            using var _ = Profiler.Scope();

            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGO.transform.SetParent(parent, false);

            var tmp = labelGO.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            var rt = labelGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, height);

            return tmp;
        }

        public static void CreateButton(Transform parent, string text, System.Action onClick, float width, float height, bool is_button_interactable = true)
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

                // Add LayoutElement for table alignment
                var layoutElem = buttonGO.GetComponent<LayoutElement>() ?? buttonGO.AddComponent<LayoutElement>();
                layoutElem.preferredWidth = width;
                layoutElem.minWidth = width;

                var btn = buttonGO.GetComponent<KButton>();
                btn.ClearOnClick();
                btn.onClick += onClick;
                btn.interactable = is_button_interactable;

                var locTexts = buttonGO.GetComponentsInChildren<LocText>(true);
                if (locTexts.Length > 0)
                {
                    locTexts[0].SetText(text);
                    locTexts[0].fontSize = 13;
                }
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

                // Add LayoutElement for table alignment
                var layoutElem = buttonGO.AddComponent<LayoutElement>();
                layoutElem.preferredWidth = width;
                layoutElem.minWidth = width;

                var image = buttonGO.GetComponent<Image>();
                image.color = new Color(0.2f, 0.35f, 0.55f);

                var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                labelGO.transform.SetParent(buttonGO.transform, false);
                var labelRT = labelGO.GetComponent<RectTransform>();
                labelRT.anchorMin = Vector2.zero;
                labelRT.anchorMax = Vector2.one;
                labelRT.sizeDelta = Vector2.zero;

                var tmp = labelGO.GetComponent<TextMeshProUGUI>();
                tmp.text = text;
                tmp.fontSize = 13;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;

                var button = buttonGO.GetComponent<Button>();
                button.onClick.AddListener(() => onClick?.Invoke());
                button.interactable = is_button_interactable;
            }
        }
    }
}
