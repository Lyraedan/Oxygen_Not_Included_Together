using ONI_MP.DebugTools;
using Shared.Profiling;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ONI_MP.Menus
{
    /// <summary>
    /// Main multiplayer screen with all lobby options.
    /// Accessed from the main menu via a single "Multiplayer" button.
    /// </summary>
    public class MultiplayerScreen : MonoBehaviour
    {
        private static MultiplayerScreen _instance;
        private static GameObject _screenGO;

        public static void Show(Transform parent)
        {
            using var _ = Profiler.Scope();

            if (_instance != null)
            {
                DebugConsole.Log("[MultiplayerScreen] Screen already open.");
                return;
            }

            _screenGO = CreateScreen(parent);
            _instance = _screenGO.AddComponent<MultiplayerScreen>();
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

            // Create full-screen overlay
            GameObject screen = new GameObject("MultiplayerScreen", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
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
            contentRT.sizeDelta = new Vector2(400, 500);

            var layout = contentGO.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 30, 30);
            layout.spacing = 20;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            // Title
            CreateTMPLabel(contentGO.transform, STRINGS.UI.MULTIPLAYERMENU.TITLE, 32, TextAlignmentOptions.Center, 50);

            // Divider
            CreateDivider(contentGO.transform);

            // Host World Button
            CreateMenuButton(contentGO.transform, STRINGS.UI.MULTIPLAYERMENU.HOST_WORLD, STRINGS.UI.MULTIPLAYERMENU.HOST_WORLD_FLAVOR, () =>
            {
                var parent = _screenGO.transform.parent;
                Close();
                // Open lobby configuration screen first
                HostLobbyConfigScreen.Show(parent);
            });

            // Divider
            CreateDivider(contentGO.transform);

            // Browse Lobbies Button
            CreateMenuButton(contentGO.transform, STRINGS.UI.MULTIPLAYERMENU.BROWSE_LOBBIES, STRINGS.UI.MULTIPLAYERMENU.BROWSE_LOBBIES_FLAVOR, () =>
            {
                var parent = _screenGO.transform.parent;
                Close();
                LobbyBrowserScreen.Show(parent);
            });

            // Join by Code Button
            CreateMenuButton(contentGO.transform, STRINGS.UI.MULTIPLAYERMENU.JOIN_BY_CODE, STRINGS.UI.MULTIPLAYERMENU.JOIN_BY_CODE_FLAVOR, () =>
            {
                var parent = _screenGO.transform.parent;
                Close();
                JoinByCodeDialog.Show(parent);
            });

            // Join via Steam Button
            CreateMenuButton(contentGO.transform, STRINGS.UI.MULTIPLAYERMENU.JOIN_BY_STEAM, STRINGS.UI.MULTIPLAYERMENU.JOIN_BY_STEAM_FLAVOR, () =>
            {
                SteamFriends.ActivateGameOverlay("friends");
            });

            // Divider
            CreateDivider(contentGO.transform);

            // Back Button
            CreateBackButton(contentGO.transform);
        }

        private void CreateMenuButton(Transform parent, string title, string subtitle, System.Action onClick)
        {
            using var _ = Profiler.Scope();

            // Try to clone from MainMenu button
            var mainMenu = FindFirstObjectByType<MainMenu>();
            var templateButton = mainMenu?.Button_NewGame;

            if (templateButton != null)
            {
                var buttonGO = Instantiate(templateButton.gameObject, parent);
                buttonGO.name = $"Button_{title.Replace(" ", "")}";

                var rt = buttonGO.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(350, 60);

                var btn = buttonGO.GetComponent<KButton>();
                btn.ClearOnClick();
                btn.onClick += onClick;

                var locTexts = buttonGO.GetComponentsInChildren<LocText>(true);
                if (locTexts.Length > 0)
                    locTexts[0].SetText(title);
                if (locTexts.Length > 1)
                    locTexts[1].SetText(subtitle);
            }
            else
            {
                // Fallback: create simple button
                CreateFallbackButton(parent, title, onClick);
            }
        }

        private void CreateBackButton(Transform parent)
        {
            using var _ = Profiler.Scope();

            var mainMenu = FindFirstObjectByType<MainMenu>();
            var templateButton = mainMenu?.Button_NewGame;

            if (templateButton != null)
            {
                var buttonGO = Instantiate(templateButton.gameObject, parent);
                buttonGO.name = "Button_Back";

                var rt = buttonGO.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(200, 50);

                var btn = buttonGO.GetComponent<KButton>();
                btn.ClearOnClick();
                btn.onClick += () => Close();

                var locTexts = buttonGO.GetComponentsInChildren<LocText>(true);
                if (locTexts.Length > 0)
                    locTexts[0].SetText(STRINGS.UI.MULTIPLAYERMENU.BACK);
                if (locTexts.Length > 1)
                    locTexts[1].SetText("");
            }
            else
            {
                CreateFallbackButton(parent, STRINGS.UI.MULTIPLAYERMENU.BACK, () => Close());
            }
        }

        private void CreateFallbackButton(Transform parent, string text, System.Action onClick)
        {
            using var _ = Profiler.Scope();

            var buttonGO = new GameObject($"Button_{text}", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGO.transform.SetParent(parent, false);

            var rt = buttonGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(350, 50);

            var image = buttonGO.GetComponent<Image>();
            image.color = new Color(0.2f, 0.35f, 0.5f);

            // Create label using TMP, not LocText
            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGO.transform.SetParent(buttonGO.transform, false);
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.sizeDelta = Vector2.zero;

            var tmp = labelGO.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 20;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            var button = buttonGO.GetComponent<Button>();
            button.onClick.AddListener(() => onClick?.Invoke());
        }

        private void CreateTMPLabel(Transform parent, string text, int fontSize, TextAlignmentOptions alignment, float height)
        {
            using var _ = Profiler.Scope();

            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGO.transform.SetParent(parent, false);

            var tmp = labelGO.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
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
    }
}
