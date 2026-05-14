using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Transport.Steamworks;
using Shared.Profiling;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ONI_MP.Menus
{
    /// <summary>
    /// In-game multiplayer info screen with host controls.
    /// Shows lobby code, connected players, and action buttons.
    /// </summary>
    public class MultiplayerInfoScreen : MonoBehaviour
    {
        private static MultiplayerInfoScreen _instance;
        private static GameObject _screenGO;
        private static GameObject _panelGO;

        public static void Show(Transform parent)
        {
            using var _ = Profiler.Scope();

            if (_instance != null)
            {
                Close();
                return; // Toggle off
            }

            _panelGO = CreateScreen(parent);
            _instance = _panelGO.AddComponent<MultiplayerInfoScreen>();
            _instance.Initialize();
        }

        public static void Close()
        {
            using var _ = Profiler.Scope();

            if (_screenGO != null)
            {
                Destroy(_screenGO);
                _screenGO = null;
                _panelGO = null;
                _instance = null;
            }
        }

        private static GameObject CreateScreen(Transform parent)
        {
            using var _ = Profiler.Scope();

            // Find the root canvas for true fullscreen overlay
            Canvas rootCanvas = null;
            if (GameScreenManager.Instance != null)
            {
                var ssLayerCanvas = GameScreenManager.Instance.ssOverlayCanvas;
                if (ssLayerCanvas != null)
                    rootCanvas = ssLayerCanvas.GetComponent<Canvas>();
            }
            if (rootCanvas == null)
                rootCanvas = parent.GetComponentInParent<Canvas>();

            Transform overlayParent = rootCanvas != null ? rootCanvas.transform : parent;

            // Fullscreen overlay to block interaction with game
            GameObject overlay = new GameObject("MultiplayerInfoOverlay", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            overlay.transform.SetParent(overlayParent, false);

            var overlayRT = overlay.GetComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.sizeDelta = Vector2.zero;
            overlayRT.anchoredPosition = Vector2.zero;

            var overlayImage = overlay.GetComponent<Image>();
            overlayImage.color = new Color(0.02f, 0.02f, 0.05f, 0.85f);

            var overlayCanvasGroup = overlay.GetComponent<CanvasGroup>();
            overlayCanvasGroup.interactable = true;
            overlayCanvasGroup.blocksRaycasts = true;

            // Centered panel (fixed position at dead center)
            GameObject screen = new GameObject("MultiplayerInfoScreen", typeof(RectTransform), typeof(Image));
            screen.transform.SetParent(overlay.transform, false);

            var rt = screen.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(420, 360);

            var image = screen.GetComponent<Image>();
            image.color = new Color(0.1f, 0.1f, 0.15f, 1f);

            // Store overlay as the main GO to destroy
            _screenGO = overlay;

            return screen;
        }

        private static void CreateCloseXButton(Transform panelTransform)
        {
            using var _ = Profiler.Scope();

            var btnGO = new GameObject("CloseXButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            btnGO.transform.SetParent(panelTransform, false);

            // Ignore layout so it positions independently
            var layoutElem = btnGO.GetComponent<LayoutElement>();
            layoutElem.ignoreLayout = true;

            var rt = btnGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-8, -8);
            rt.sizeDelta = new Vector2(28, 28);

            var image = btnGO.GetComponent<Image>();
            image.color = new Color(0.6f, 0.25f, 0.25f, 1f);

            var textGO = new GameObject("X", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGO.transform.SetParent(btnGO.transform, false);
            var textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = Vector2.zero;

            var tmp = textGO.GetComponent<TextMeshProUGUI>();
            tmp.text = "X";
            tmp.fontSize = 16;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            var button = btnGO.GetComponent<Button>();
            button.onClick.AddListener(Close);
        }

        private void Initialize()
        {
            using var _ = Profiler.Scope();

            // Main layout on the panel
            var layout = _panelGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.spacing = 15;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = false;
            layout.childControlWidth = true;

            // Title
            CreateLabel(STRINGS.UI.SERVERBROWSER.MULTIPLAYER_SESSION_TITLE, 24, FontStyles.Bold, 35);

            // Divider
            CreateDivider();

            // Lobby Code section
            CreateLobbyCodeSection();

            // Connected players
            CreatePlayersSection();

            // Divider
            CreateDivider();

            // Action buttons
            CreateActionButtons();

            // X close button at top-right (created last for proper raycast order)
            CreateCloseXButton(_panelGO.transform);
        }

        private void CreateLobbyCodeSection()
        {
            using var _ = Profiler.Scope();

            var container = new GameObject("CodeSection", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            container.transform.SetParent(_panelGO.transform, false);

            var containerRT = container.GetComponent<RectTransform>();
            containerRT.sizeDelta = new Vector2(0, 50);

            var layout = container.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;

            // Label
            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGO.transform.SetParent(container.transform, false);
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.sizeDelta = new Vector2(100, 40);
            var labelTMP = labelGO.GetComponent<TextMeshProUGUI>();
            labelTMP.text = STRINGS.UI.SERVERBROWSER.LOBBY_CODE;
            labelTMP.fontSize = 16;
            labelTMP.alignment = TextAlignmentOptions.MidlineRight;
            labelTMP.color = new Color(0.7f, 0.7f, 0.7f);

            // Code display
            var codeGO = new GameObject("Code", typeof(RectTransform), typeof(Image));
            codeGO.transform.SetParent(container.transform, false);
            var codeRT = codeGO.GetComponent<RectTransform>();
            codeRT.sizeDelta = new Vector2(180, 40);
            var codeImage = codeGO.GetComponent<Image>();
            codeImage.color = new Color(0.15f, 0.15f, 0.2f);

            var codeTextGO = new GameObject("CodeText", typeof(RectTransform), typeof(TextMeshProUGUI));
            codeTextGO.transform.SetParent(codeGO.transform, false);
            var codeTextRT = codeTextGO.GetComponent<RectTransform>();
            codeTextRT.anchorMin = Vector2.zero;
            codeTextRT.anchorMax = Vector2.one;
            codeTextRT.sizeDelta = Vector2.zero;
            var codeTMP = codeTextGO.GetComponent<TextMeshProUGUI>();
            string formattedCode = LobbyCodeHelper.FormatCodeForDisplay(SteamLobby.CurrentLobbyCode);
            codeTMP.text = formattedCode;
            codeTMP.fontSize = 20;
            codeTMP.alignment = TextAlignmentOptions.Center;
            codeTMP.color = new Color(0.5f, 0.9f, 1f);
            codeTMP.fontStyle = FontStyles.Bold;

            // Copy button
            CreateCopyButton(container.transform, SteamLobby.CurrentLobbyCode);
        }

        private void CreateCopyButton(Transform parent, string textToCopy)
        {
            using var _ = Profiler.Scope();

            var btnGO = new GameObject("CopyButton", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGO.transform.SetParent(parent, false);

            var rt = btnGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(70, 35);

            var image = btnGO.GetComponent<Image>();
            image.color = new Color(0.3f, 0.5f, 0.7f);

            var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGO.transform.SetParent(btnGO.transform, false);
            var textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = Vector2.zero;
            var textTMP = textGO.GetComponent<TextMeshProUGUI>();
            textTMP.text = STRINGS.UI.SERVERBROWSER.COPY;
            textTMP.fontSize = 14;
            textTMP.alignment = TextAlignmentOptions.Center;
            textTMP.color = Color.white;

            var button = btnGO.GetComponent<Button>();
            button.onClick.AddListener(() =>
            {
                GUIUtility.systemCopyBuffer = textToCopy;
                textTMP.text = STRINGS.UI.SERVERBROWSER.COPIED;
                // Reset after delay
                StartCoroutine(ResetCopyButtonText(textTMP));
            });
        }

        private System.Collections.IEnumerator ResetCopyButtonText(TextMeshProUGUI tmp)
        {
            using var _ = Profiler.Scope();

            yield return new WaitForSeconds(1.5f);
            if (tmp != null)
                tmp.text = STRINGS.UI.SERVERBROWSER.COPY;
        }

        private void CreatePlayersSection()
        {
            using var _ = Profiler.Scope();

            var container = new GameObject("PlayersSection", typeof(RectTransform));
            container.transform.SetParent(_panelGO.transform, false);
            var containerRT = container.GetComponent<RectTransform>();
            containerRT.sizeDelta = new Vector2(0, 30);

            var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGO.transform.SetParent(container.transform, false);
            var textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = Vector2.zero;

            var tmp = textGO.GetComponent<TextMeshProUGUI>();
            int playerCount = MultiplayerSession.ConnectedPlayers.Count + 1; // +1 for host
            tmp.text = string.Format(STRINGS.UI.SERVERBROWSER.CONNECTED_PLAYERS, playerCount);
            tmp.fontSize = 16;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.8f, 0.8f, 0.8f);
        }

        private void CreateActionButtons()
        {
            using var _ = Profiler.Scope();

            var container = new GameObject("ActionButtons", typeof(RectTransform), typeof(VerticalLayoutGroup));
            container.transform.SetParent(_panelGO.transform, false);

            var containerRT = container.GetComponent<RectTransform>();
            containerRT.sizeDelta = new Vector2(0, 150);

            var layout = container.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            if (MultiplayerSession.IsHost)
            {
                // Invite button
                CreateActionButton(container.transform, STRINGS.UI.PAUSESCREEN.INVITE.LABEL, () =>
                {
                    Steamworks.SteamFriends.ActivateGameOverlayInviteDialog(SteamLobby.CurrentLobby);
                });

                // Hard Sync button
                if (!GameServerHardSync.hardSyncDoneThisCycle)
                {
                    CreateActionButton(container.transform, STRINGS.UI.PAUSESCREEN.DOHARDSYNC.LABEL, () =>
                    {
                        Close();
                        if (MultiplayerSession.ConnectedPlayers.Count > 0)
                        {
                            GameServerHardSync.PerformHardSync(true);
                        }
                        else
                        {
                            GameServerHardSync.hardSyncDoneThisCycle = true;
                        }
                    });
                }
                else
                {
                    CreateActionButton(container.transform, STRINGS.UI.PAUSESCREEN.HARDSYNCNOTAVAILABLE.LABEL, null, true);
                }

                // End Session button
                CreateActionButton(container.transform, STRINGS.UI.PAUSESCREEN.ENDSESSION.LABEL, () =>
                {
                    Close();
                    NetworkConfig.Stop();
                }, false, new Color(0.7f, 0.3f, 0.3f));
            }
            else
            {
                // Leave Session button for clients
                CreateActionButton(container.transform, STRINGS.UI.PAUSESCREEN.LEAVESESSION.LABEL, () =>
                {
                    Close();
                    NetworkConfig.Stop();
                }, false, new Color(0.7f, 0.3f, 0.3f));
            }
        }

        private void CreateActionButton(Transform parent, string text, System.Action onClick, bool disabled = false, Color? bgColor = null)
        {
            using var _ = Profiler.Scope();

            var btnGO = new GameObject($"Btn_{text}", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGO.transform.SetParent(parent, false);

            var rt = btnGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(280, 38);

            var image = btnGO.GetComponent<Image>();
            image.color = bgColor ?? (disabled ? new Color(0.2f, 0.2f, 0.25f) : new Color(0.25f, 0.4f, 0.55f));

            var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGO.transform.SetParent(btnGO.transform, false);
            var textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = Vector2.zero;

            var tmp = textGO.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 16;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = disabled ? new Color(0.5f, 0.5f, 0.5f) : Color.white;

            var button = btnGO.GetComponent<Button>();
            button.interactable = !disabled && onClick != null;
            if (onClick != null)
                button.onClick.AddListener(() => onClick());
        }

        private void CreateLabel(string text, int fontSize, FontStyles style, float height)
        {
            using var _ = Profiler.Scope();

            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGO.transform.SetParent(_panelGO.transform, false);

            var rt = labelGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, height);

            var tmp = labelGO.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
        }

        private void CreateDivider()
        {
            using var _ = Profiler.Scope();

            var dividerGO = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            dividerGO.transform.SetParent(_panelGO.transform, false);

            var rt = dividerGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 2);

            var image = dividerGO.GetComponent<Image>();
            image.color = new Color(0.3f, 0.3f, 0.4f, 0.5f);
        }
    }
}
