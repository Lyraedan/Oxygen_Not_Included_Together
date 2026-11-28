using ONI_Together;
using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Transport;
using ONI_Together.Networking.Transport.External;
using Shared.Helpers;
using Shared.Profiling;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using ONI_Together.UI.lib;
using UI.lib.UI.FUI;
using UI.lib.UIcmp;
using UnityEngine;
using UnityEngine.UI;

namespace ONI_Together.UI
{
    /// <summary>
    /// Runtime-built Custom Transport host/join tabs. The LAN tab is cloned only as a
    /// container; all connection fields come from the provider's setting schema.
    /// Set EnableRuntimeCustomTab to false and wire the prefab references instead once
    /// the Custom tab exists in the Unity UI asset.
    /// </summary>
    internal partial class UnityMultiplayerScreen
    {
        private const bool EnableRuntimeCustomTab = true;

        private GameObject HostCustomTab;
        private GameObject JoinCustomTab;
        private FToggleButton HostCustomToggle;
        private FToggleButton JoinCustomToggle;

        private Transform HostCustomContent;
        private Transform JoinCustomContent;
        private Transform HostCustomSettingsRoot;
        private Transform JoinCustomSettingsRoot;

        private string SelectedCustomHostProviderId = string.Empty;
        private string SelectedCustomJoinProviderId = string.Empty;
        private Dictionary<string, string> ActiveHostSettings = new();
        private Dictionary<string, string> ActiveJoinSettings = new();

        private GameObject HostLoginStatusRow;
        private GameObject JoinLoginStatusRow;
        private LocText HostLoginStatusLabel;
        private LocText JoinLoginStatusLabel;
        private FButton HostLoginButton;
        private FButton JoinLoginButton;
        private FButton JoinCustomButton;

        private void InitializeCustomTransportTabs()
        {
            using var _ = Profiler.Scope();

            if (!EnableRuntimeCustomTab)
                return;

            HostCustomTab = CreateHostCustomTab(StartHostingSegment);
            JoinCustomTab = CloneTab(LanTab, "CustomJoin");

            // Insert the custom tabs beside their LAN counterparts. Creating/cloning appends
            // at the end of the layout group, which would put them after the Cancel button.
            if (HostCustomTab != null && HostLanTab != null)
                HostCustomTab.transform.SetSiblingIndex(HostLanTab.transform.GetSiblingIndex() + 1);
            if (JoinCustomTab != null && LanTab != null)
                JoinCustomTab.transform.SetSiblingIndex(LanTab.transform.GetSiblingIndex() + 1);

            HostCustomToggle = CloneToggle(HostLanToggle, "Custom", () => SetHostVia(HostMode.Custom));
            JoinCustomToggle = CloneToggle(LanTabToggle, "Custom", () => SetJoinVia(JoinMode.Custom));

            ConfigureSeparatePanels();

            HostCustomTab.SetActive(false);
            JoinCustomTab.SetActive(false);
        }

        private void ConfigureSeparatePanels()
        {
            // The root previously drew one big background/outline around both panels.
            // Disable it so each menu keeps its own rectangle, background and outline.
            var rootImage = transform.GetComponent<Image>();
            if (rootImage != null)
                rootImage.enabled = false;

            var rootOutline = transform.GetComponent<Outline>();
            if (rootOutline != null)
                rootOutline.enabled = false;

            // Remove the extra root spacing so the only separation is the thin black
            // MainSpacer divider, not a strip of the game background.
            var rootLayout = transform.GetComponent<HorizontalLayoutGroup>();
            if (rootLayout != null)
                rootLayout.spacing = 0f;

            AddPanelOutline(MainMenuSegment);
            AddPanelOutline(StartHostingSegment);
            AddPanelOutline(LobbyBrowserSegment);
            AddPanelOutline(AdditionalHostSettingsSegment);
        }

        private static void AddPanelOutline(GameObject panel)
        {
            if (panel == null)
                return;

            var outline = panel.GetComponent<Outline>() ?? panel.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.5f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
        }

        private void InitializeLanJoinDisplayName()
        {
            if (NumberInputPrefab == null || LanTab == null)
                return;

            var go = Util.KInstantiateUI(NumberInputPrefab, LanTab, true);
            go.name = "DisplayName";

            var label = go.transform.Find("Label")?.gameObject.GetComponent<LocText>();
            label?.SetText(STRINGS.UI.CONFIGURATION.TITLES.LAN_SETTINGS.LAN_DISPLAY_NAME);

            var input = go.transform.Find("Input")?.gameObject.AddOrGet<FInputField2>();
            if (input == null)
                return;

            input.inputField.contentType = TMP_InputField.ContentType.Standard;
            input.inputField.characterValidation = TMP_InputField.CharacterValidation.None;
            input.inputField.characterLimit = Utils.MaxLocalPlayerNameLength;
            input.Text = Configuration.Instance.Host.LanSettings.DisplayName ?? string.Empty;

            var placeholder = input.transform.Find("TextArea/Placeholder")?.gameObject.GetComponent<LocText>();
            placeholder?.SetText("Player");

            input.AddListener(text =>
            {
                Configuration.Instance.Host.LanSettings.DisplayName = text ?? string.Empty;
                Configuration.Instance.Save();

                if (SettingsTextInputs.TryGetValue("LanDisplayName", out var hostInput))
                    hostInput.SetTextFromData(text ?? string.Empty);
            });

            var element = go.AddOrGet<LayoutElement>();
            element.minHeight = 50f;
            element.preferredHeight = 50f;
            element.flexibleHeight = 0f;

            var inputs = LanTab.transform.Find("Inputs");
            go.transform.SetSiblingIndex(inputs != null ? inputs.GetSiblingIndex() : 0);

            JoinDisplayNameInput = input;
        }

        private static GameObject CreateHostCustomTab(GameObject hostMenu)
        {
            if (hostMenu == null)
                return null;

            var tab = new GameObject(
                "CustomHosting",
                typeof(RectTransform),
                typeof(VerticalLayoutGroup));

            tab.transform.SetParent(hostMenu.transform, false);

            var rect = tab.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(0f, 112f);

            var layout = tab.GetComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 12f;

            tab.SetActive(false);
            return tab;
        }

        private static GameObject CloneTab(GameObject source, string name)
        {
            if (source == null)
                return null;

            var clone = Instantiate(source, source.transform.parent);
            clone.name = name;

            // The cloned LAN tab is only used as a container. Hide and remove its
            // LAN-specific children; the Custom tab is entirely schema-driven.
            for (int i = clone.transform.childCount - 1; i >= 0; i--)
            {
                var child = clone.transform.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            return clone;
        }

        private static FToggleButton CloneToggle(FToggleButton source, string label, System.Action onClick)
        {
            if (source == null)
                return null;

            var clone = Instantiate(source.gameObject, source.transform.parent);
            clone.name = label + "CustomToggle";

            var button = clone.GetComponent<FToggleButton>();
            var labelText = clone.GetComponentInChildren<LocText>(true);
            labelText?.SetText(label);

            var sourceRect = source.GetComponent<RectTransform>();
            var cloneRect = clone.GetComponent<RectTransform>();
            if (sourceRect != null && cloneRect != null)
            {
                cloneRect.anchoredPosition = sourceRect.anchoredPosition
                    + new Vector2(sourceRect.rect.width + 10f, 0f);
            }

            if (button != null && onClick != null)
                button.OnClick += onClick;

            return button;
        }

        private bool CanStartHosting()
        {
            if (CurrentHostMode != HostMode.Custom)
                return true;

            if (TransportRegistry.GetAllExternalProviders().Count == 0)
                return false;

            var provider = TransportRegistry.GetExternalProvider(SelectedCustomHostProviderId);
            if (provider == null)
                return false;

            return !provider.RequiresLogin
                || provider.LoginState == ExternalTransportLoginState.Ready;
        }

        private bool CanJoinCustom()
        {
            var provider = TransportRegistry.GetExternalProvider(SelectedCustomJoinProviderId);
            if (provider == null)
                return false;

            return !provider.RequiresLogin
                || provider.LoginState == ExternalTransportLoginState.Ready;
        }

        private void UpdateStartHostingInteractable()
        {
            StartHosting?.SetInteractable(CanStartHosting());
        }

        private void UpdateJoinInteractable()
        {
            JoinCustomButton?.SetInteractable(CanJoinCustom());
        }

        private void RefreshCustomHostTab()
        {
            using var _ = Profiler.Scope();

            if (!EnableRuntimeCustomTab || HostCustomTab == null)
                return;

            if (HostCustomContent != null)
            {
                HostCustomContent.gameObject.SetActive(false);
                Destroy(HostCustomContent.gameObject);
            }

            HostCustomContent = CreateCustomContent(HostCustomTab, "CustomHostContent");

            var providers = TransportRegistry.GetAllExternalProviders();
            if (providers.Count == 0)
            {
                CreateInfoLabel(HostCustomContent, "No custom transports installed.");
                SelectedCustomHostProviderId = string.Empty;
                ActiveHostSettings = new Dictionary<string, string>();
                UpdateStartHostingInteractable();
                return;
            }

            string selected = Configuration.Instance.Host.CustomTransportId;
            var selectedProvider = providers.FirstOrDefault(
                p => p.Id.Equals(selected, StringComparison.OrdinalIgnoreCase));
            selected = selectedProvider?.Id ?? providers[0].Id;

            SelectedCustomHostProviderId = selected;
            var options = providers
                .Select(p => new FCycle.Option(p.Id, p.DisplayName, string.Empty))
                .ToList();

            CreateProviderCycle(HostCustomContent, "HostTransport", options, selected, OnHostProviderChanged);
            CreateLoginStatusUi(HostCustomContent, true);
            HostCustomSettingsRoot = CreateSettingsRoot(HostCustomContent);
            BuildSettingsForProvider(true, SelectedCustomHostProviderId);
            ResizeCustomTab(HostCustomTab, HostCustomContent);
            ResizeMenuPanel(StartHostingSegment);
            RefreshCustomLoginStatus(true);
            UpdateStartHostingInteractable();
        }

        private void RefreshCustomJoinTab()
        {
            using var _ = Profiler.Scope();

            if (!EnableRuntimeCustomTab || JoinCustomTab == null)
                return;

            if (JoinCustomContent != null)
            {
                JoinCustomContent.gameObject.SetActive(false);
                Destroy(JoinCustomContent.gameObject);
            }

            JoinCustomContent = CreateCustomContent(JoinCustomTab, "CustomJoinContent");

            var providers = TransportRegistry.GetAllExternalProviders();
            if (providers.Count == 0)
            {
                CreateInfoLabel(JoinCustomContent, "No custom transports installed.");
                SelectedCustomJoinProviderId = string.Empty;
                ActiveJoinSettings = new Dictionary<string, string>();
                return;
            }

            string selected = Configuration.Instance.Client.CustomTransportId;
            var selectedProvider = providers.FirstOrDefault(
                p => p.Id.Equals(selected, StringComparison.OrdinalIgnoreCase));
            selected = selectedProvider?.Id ?? providers[0].Id;

            SelectedCustomJoinProviderId = selected;
            var options = providers
                .Select(p => new FCycle.Option(p.Id, p.DisplayName, string.Empty))
                .ToList();

            CreateProviderCycle(JoinCustomContent, "JoinTransport", options, selected, OnJoinProviderChanged);
            CreateLoginStatusUi(JoinCustomContent, false);
            JoinCustomSettingsRoot = CreateSettingsRoot(JoinCustomContent);
            BuildSettingsForProvider(false, SelectedCustomJoinProviderId);
            CreateSpacer(JoinCustomContent, 10f);
            CreateJoinButton(JoinCustomContent);
            ResizeCustomTab(JoinCustomTab, JoinCustomContent);
            ResizeMenuPanel(MainMenuSegment);
            RefreshCustomLoginStatus(false);
        }

        private Transform CreateCustomContent(GameObject tab, string name)
        {
            var tabRect = tab.GetComponent<RectTransform>();
            if (tabRect != null && tabRect.rect.width < 1f)
                tabRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 410f);

            var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            go.transform.SetParent(tab.transform, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(20f, 0f);
            rect.offsetMax = new Vector2(-20f, -20f);

            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 12f;
            layout.padding = new RectOffset(5, 5, 10, 5);

            var fitter = go.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return go.transform;
        }

        private void UpdateMiddleSpacerHeight()
        {
            if (MiddleSpacer == null || !MiddleSpacer.activeSelf)
                return;

            float height = 480f;

            void Consider(GameObject panel)
            {
                if (panel == null || !panel.activeSelf)
                    return;

                var rect = panel.GetComponent<RectTransform>();
                if (rect != null)
                    height = Mathf.Max(height, rect.rect.height);
            }

            Consider(MainMenuSegment);
            Consider(StartHostingSegment);
            Consider(LobbyBrowserSegment);
            Consider(AdditionalHostSettingsSegment);

            var spacerRect = MiddleSpacer.GetComponent<RectTransform>();
            spacerRect?.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }

        private void ResizeMenuPanel(GameObject menu)
        {
            if (menu == null)
                return;

            var menuRect = menu.GetComponent<RectTransform>();
            if (menuRect == null)
                return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(menuRect);

            float preferredHeight = LayoutUtility.GetPreferredHeight(menuRect);
            float targetHeight = Mathf.Max(480f, preferredHeight + 4f);

            menuRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetHeight);

            UpdateMiddleSpacerHeight();

            if (transform is RectTransform screenRect)
            {
                var screenFitter = transform.GetComponent<ContentSizeFitter>();
                if (screenFitter != null)
                    screenFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                LayoutRebuilder.ForceRebuildLayoutImmediate(screenRect);
            }
        }

        private static void ResizeTabToPreferredHeight(GameObject tab)
        {
            if (tab == null)
                return;

            var rect = tab.GetComponent<RectTransform>();
            if (rect == null)
                return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

            float preferredHeight = LayoutUtility.GetPreferredHeight(rect);
            float targetHeight = Mathf.Max(112f, preferredHeight + 4f);

            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetHeight);

            var layoutElement = tab.GetComponent<LayoutElement>() ?? tab.AddOrGet<LayoutElement>();
            layoutElement.minHeight = targetHeight;
            layoutElement.preferredHeight = targetHeight;
            layoutElement.flexibleHeight = 0f;
        }

        private static void ResizeCustomTab(GameObject tab, Transform content)
        {
            if (tab == null || content == null)
                return;

            var contentRect = content as RectTransform;
            var tabRect = tab.GetComponent<RectTransform>();
            if (contentRect == null || tabRect == null)
                return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);

            float preferredHeight = LayoutUtility.GetPreferredHeight(contentRect);
            float targetHeight = Mathf.Max(112f, preferredHeight + 4f);

            tabRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetHeight);

            var layoutElement = tab.GetComponent<LayoutElement>() ?? tab.AddOrGet<LayoutElement>();
            layoutElement.minHeight = targetHeight;
            layoutElement.preferredHeight = targetHeight;
            layoutElement.flexibleHeight = 0f;
        }

        private Transform CreateSettingsRoot(Transform parent)
        {
            var go = new GameObject("Settings", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            go.transform.SetParent(parent, false);

            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 6f;

            var fitter = go.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return go.transform;
        }

        private void CreateInfoLabel(Transform parent, string text)
        {
            var go = Util.KInstantiateUI(NumberInputPrefab, parent.gameObject, true);
            var label = go.transform.Find("Label")?.gameObject.GetComponent<LocText>();
            label?.SetText(text);
            var input = go.transform.Find("Input");
            input?.gameObject.SetActive(false);
        }

        private void CreateProviderCycle(Transform parent, string id, List<FCycle.Option> options,
            string selected, System.Action<FCycle.Option> onChange)
        {
            var cycle = Util.KInstantiateUI(CyclePrefab, parent.gameObject, true).AddOrGet<FCycle>();
            cycle.SetDescriptionFormatter(desc => desc.Replace("\n\n", "\n"));

            var cycleLayout = cycle.gameObject.AddOrGet<LayoutElement>();
            cycleLayout.minHeight = 50f;
            cycleLayout.preferredHeight = 50f;
            cycleLayout.flexibleHeight = 0f;
            cycle.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 50f);

            var label = cycle.transform.Find("Label")?.gameObject.GetComponent<LocText>();
            label?.SetText("Type");

            cycle.Initialize(
                cycle.transform.Find("Left").gameObject.AddOrGet<FButton>(),
                cycle.transform.Find("Right").gameObject.AddOrGet<FButton>(),
                cycle.transform.Find("ChoiceLabel").gameObject.AddOrGet<LocText>(),
                cycle.transform.Find("ChoiceLabel/Description").gameObject.AddOrGet<LocText>());

            cycle.Options = options;
            cycle.OnChange += onChange;
            cycle.SetValueById(selected);
        }

        private static void CreateSpacer(Transform parent, float height)
        {
            var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var element = go.GetComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
        }

        private void CreateJoinButton(Transform parent)
        {
            if (JoinViaLan == null)
                return;

            var go = Instantiate(JoinViaLan.gameObject, parent);
            go.name = "JoinCustomButton";
            go.SetActive(true);

            var rect = go.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(1f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(0f, 50f);
            }

            var layoutElement = go.AddOrGet<LayoutElement>();
            layoutElement.minHeight = 50f;
            layoutElement.preferredHeight = 50f;
            layoutElement.flexibleHeight = 0f;

            var label = go.GetComponentInChildren<LocText>(true);
            label?.SetText("Join");

            var button = go.GetComponent<FButton>();
            if (button != null)
                button.OnClick += JoinCustomGame;

            JoinCustomButton = button;
        }

        private void CreateLoginStatusUi(Transform parent, bool host)
        {
            var row = Util.KInstantiateUI(NumberInputPrefab, parent.gameObject, true);
            row.name = host ? "HostEosLoginStatus" : "JoinEosLoginStatus";

            var label = row.transform.Find("Label")?.gameObject.GetComponent<LocText>();
            if (label != null)
            {
                label.SetText("EOS login...");
                label.textWrappingMode = TextWrappingModes.Normal;
            }

            var input = row.transform.Find("Input");
            input?.gameObject.SetActive(false);

            var rowLayout = row.AddOrGet<LayoutElement>();
            rowLayout.minHeight = 60f;
            rowLayout.preferredHeight = 70f;
            rowLayout.flexibleHeight = 0f;
            row.SetActive(false);

            FButton button = null;
            if (JoinViaLan != null)
            {
                var buttonGo = Instantiate(JoinViaLan.gameObject, parent);
                buttonGo.name = host ? "HostEosLoginButton" : "JoinEosLoginButton";
                buttonGo.SetActive(false);

                var rect = buttonGo.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchorMin = new Vector2(0f, 0.5f);
                    rect.anchorMax = new Vector2(1f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.sizeDelta = new Vector2(0f, 50f);
                }

                var layout = buttonGo.AddOrGet<LayoutElement>();
                layout.minHeight = 50f;
                layout.preferredHeight = 50f;
                layout.flexibleHeight = 0f;

                var buttonLabel = buttonGo.GetComponentInChildren<LocText>(true);
                buttonLabel?.SetText("Log In to EOS");

                button = buttonGo.GetComponent<FButton>();
                if (button != null)
                    button.OnClick += () => BeginLoginFor(host);
            }

            if (host)
            {
                HostLoginStatusRow = row;
                HostLoginStatusLabel = label;
                HostLoginButton = button;
            }
            else
            {
                JoinLoginStatusRow = row;
                JoinLoginStatusLabel = label;
                JoinLoginButton = button;
            }
        }

        private void BeginLoginFor(bool host)
        {
            var provider = TransportRegistry.GetExternalProvider(
                host ? SelectedCustomHostProviderId : SelectedCustomJoinProviderId);
            if (provider == null || !provider.RequiresLogin)
                return;

            provider.BeginLogin();
            RefreshCustomLoginStatus(host);
        }

        private void RefreshCustomLoginStatus(bool host)
        {
            if (host ? CurrentHostMode != HostMode.Custom : CurrentJoinMode != JoinMode.Custom)
                return;

            var provider = TransportRegistry.GetExternalProvider(
                host ? SelectedCustomHostProviderId : SelectedCustomJoinProviderId);
            var row = host ? HostLoginStatusRow : JoinLoginStatusRow;
            var label = host ? HostLoginStatusLabel : JoinLoginStatusLabel;
            var button = host ? HostLoginButton : JoinLoginButton;

            if (row == null)
                return;

            bool requires = provider != null && provider.RequiresLogin;
            bool visibilityChanged = false;

            if (!requires)
            {
                if (row.activeSelf)
                {
                    row.SetActive(false);
                    visibilityChanged = true;
                }

                if (button != null && button.gameObject.activeSelf)
                {
                    button.gameObject.SetActive(false);
                    visibilityChanged = true;
                }

                if (host)
                    UpdateStartHostingInteractable();
                else
                    UpdateJoinInteractable();

                if (visibilityChanged)
                    ResizeCustomTabFor(host);

                return;
            }

            if (!row.activeSelf)
            {
                row.SetActive(true);
                visibilityChanged = true;
            }

            var state = provider.LoginState;
            if (state == ExternalTransportLoginState.NotStarted)
            {
                provider.BeginLogin();
                state = provider.LoginState;
            }

            string text = provider.LoginMessage;
            if (string.IsNullOrWhiteSpace(text))
            {
                switch (state)
                {
                    case ExternalTransportLoginState.NotStarted:
                        text = "EOS login required.";
                        break;
                    case ExternalTransportLoginState.LoggingIn:
                        text = "Logging in to EOS...";
                        break;
                    case ExternalTransportLoginState.Ready:
                        text = "Logged in to EOS.";
                        break;
                    default:
                        text = "EOS login failed.";
                        break;
                }
            }

            label?.SetText(text);

            if (button != null)
            {
                bool showButton = state != ExternalTransportLoginState.Ready;
                if (button.gameObject.activeSelf != showButton)
                {
                    button.gameObject.SetActive(showButton);
                    visibilityChanged = true;
                }

                if (showButton)
                {
                    var buttonLabel = button.GetComponentInChildren<LocText>(true);
                    buttonLabel?.SetText(
                        state == ExternalTransportLoginState.Failed ? "Retry EOS Login"
                        : state == ExternalTransportLoginState.LoggingIn ? "Logging in..."
                        : "Log In to EOS");

                    button.SetInteractable(state != ExternalTransportLoginState.LoggingIn);
                }
            }

            if (host)
                UpdateStartHostingInteractable();
            else
                UpdateJoinInteractable();

            if (visibilityChanged)
                ResizeCustomTabFor(host);
        }

        private void ResizeCustomTabFor(bool host)
        {
            ResizeCustomTab(
                host ? HostCustomTab : JoinCustomTab,
                host ? HostCustomContent : JoinCustomContent);
            ResizeMenuPanel(host ? StartHostingSegment : MainMenuSegment);
        }

        private void OnHostProviderChanged(FCycle.Option option)
        {
            SelectedCustomHostProviderId = option.id;
            Configuration.Instance.Host.CustomTransportId = option.id;
            Configuration.Instance.Save();
            BuildSettingsForProvider(true, option.id);
            RefreshCustomLoginStatus(true);
            UpdateStartHostingInteractable();
        }

        private void OnJoinProviderChanged(FCycle.Option option)
        {
            SelectedCustomJoinProviderId = option.id;
            Configuration.Instance.Client.CustomTransportId = option.id;
            Configuration.Instance.Save();
            BuildSettingsForProvider(false, option.id);
            RefreshCustomLoginStatus(false);
        }

        private void BuildSettingsForProvider(bool host, string providerId)
        {
            using var _ = Profiler.Scope();

            var provider = TransportRegistry.GetExternalProvider(providerId);
            var root = host ? HostCustomSettingsRoot : JoinCustomSettingsRoot;
            if (provider == null || root == null)
                return;

            ClearChildren(root);

            var config = Configuration.Instance;
            var allSettings = host
                ? config.Host.CustomTransportSettings
                : config.Client.CustomTransportSettings;

            if (allSettings == null)
            {
                allSettings = new Dictionary<string, Dictionary<string, string>>();
                if (host)
                    config.Host.CustomTransportSettings = allSettings;
                else
                    config.Client.CustomTransportSettings = allSettings;
            }

            if (!allSettings.TryGetValue(providerId, out var values) || values == null)
            {
                values = new Dictionary<string, string>();
                allSettings[providerId] = values;
            }

            if (host)
                ActiveHostSettings = values;
            else
                ActiveJoinSettings = values;

            var settings = provider.GetSettings()
                .Where(setting => SettingApplies(setting.Scope, host))
                .ToList();

            string GetValue(ExternalTransportSetting setting)
            {
                string value = values.TryGetValue(setting.Key, out var saved)
                    ? saved
                    : setting.DefaultValue;

                if (!values.ContainsKey(setting.Key))
                    values[setting.Key] = value ?? string.Empty;

                return value;
            }

            void SetValue(ExternalTransportSetting setting, string value)
            {
                values[setting.Key] = value ?? string.Empty;
                SaveCustomSettings(host);
            }

            // Address and port are the common pair; put them on one row so the
            // Custom tab still fits the LAN tab's height.
            var address = settings.FirstOrDefault(setting => setting.Key == "address");
            var port = settings.FirstOrDefault(setting => setting.Key == "port");
            bool hasAddressPortRow = address != null && port != null;

            if (hasAddressPortRow)
            {
                var row = CreateHorizontalRow(root);
                CreateSettingControl(row, address, GetValue(address),
                    value => SetValue(address, value), halfWidth: true);
                CreateSettingControl(row, port, GetValue(port),
                    value => SetValue(port, value), halfWidth: true);
            }

            foreach (var setting in settings)
            {
                // Only skip the pair when it was actually rendered as one row. On the
                // host side "address" (client-only) is filtered out, so "port" must
                // still be drawn as its own field.
                if (hasAddressPortRow && (setting == address || setting == port))
                    continue;

                CreateSettingControl(root, setting, GetValue(setting),
                    value => SetValue(setting, value));
            }

            ResizeCustomTab(
                host ? HostCustomTab : JoinCustomTab,
                host ? HostCustomContent : JoinCustomContent);
            ResizeMenuPanel(host ? StartHostingSegment : MainMenuSegment);
        }

        private Transform CreateHorizontalRow(Transform parent)
        {
            var go = new GameObject(
                "SettingRow",
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup),
                typeof(LayoutElement));

            go.transform.SetParent(parent, false);

            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 6f;

            var element = go.GetComponent<LayoutElement>();
            element.minHeight = 50f;
            element.preferredHeight = 50f;

            return go.transform;
        }

        private static bool SettingApplies(ExternalTransportSettingScope scope, bool host)
        {
            return host
                ? (scope & ExternalTransportSettingScope.Host) != 0
                : (scope & ExternalTransportSettingScope.Client) != 0;
        }

        private void CreateSettingControl(Transform parent, ExternalTransportSetting setting, string value,
            System.Action<string> onChange, bool halfWidth = false)
        {
            switch (setting.Kind)
            {
                case ExternalTransportSettingKind.Boolean:
                    CreateToggle(parent, setting, value, onChange);
                    break;
                case ExternalTransportSettingKind.Integer:
                    CreateTextInput(parent, setting, value, onChange,
                        TMP_InputField.ContentType.IntegerNumber, halfWidth);
                    break;
                case ExternalTransportSettingKind.Password:
                    CreateTextInput(parent, setting, value, onChange,
                        TMP_InputField.ContentType.Password, halfWidth);
                    break;
                default:
                    CreateTextInput(parent, setting, value, onChange,
                        TMP_InputField.ContentType.Standard, halfWidth);
                    break;
            }
        }

        private void CreateTextInput(Transform parent, ExternalTransportSetting setting, string value,
            System.Action<string> onChange, TMP_InputField.ContentType contentType, bool halfWidth)
        {
            var go = Util.KInstantiateUI(NumberInputPrefab, parent.gameObject, true);
            if (halfWidth)
            {
                var element = go.gameObject.AddOrGet<LayoutElement>();
                element.flexibleWidth = 1f;
                element.minWidth = 0f;
                element.preferredWidth = 0f;
            }

            var label = go.transform.Find("Label")?.gameObject.GetComponent<LocText>();
            label?.SetText(setting.Label);

            var input = go.transform.Find("Input")?.gameObject.AddOrGet<FInputField2>();
            if (input == null)
                return;

            input.inputField.contentType = contentType;
            input.inputField.characterLimit = 512;
            input.Text = value ?? string.Empty;

            var placeholder = input.transform.Find("TextArea/Placeholder")?.gameObject.GetComponent<LocText>();
            placeholder?.SetText(setting.Placeholder ?? string.Empty);

            input.AddListener(text => onChange?.Invoke(text ?? string.Empty));
        }

        private void CreateToggle(Transform parent, ExternalTransportSetting setting, string value,
            System.Action<string> onChange)
        {
            var go = Util.KInstantiateUI(TogglePrefab, parent.gameObject, true);
            var toggle = go.gameObject.AddOrGet<FToggle>();

            var label = toggle.transform.Find("Label")?.gameObject.GetComponent<LocText>();
            label?.SetText(setting.Label);

            toggle.SetCheckmark("Background/Checkmark");
            toggle.SetOnFromCode(ParseBool(value));
            toggle.OnClick += enabled => onChange?.Invoke(enabled ? "1" : "0");
        }

        private static bool ParseBool(string value)
        {
            return value == "1"
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private void ClearChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        private void SaveCustomSettings(bool host)
        {
            var config = Configuration.Instance;
            if (host)
            {
                config.Host.CustomTransportId = SelectedCustomHostProviderId;
                if (!string.IsNullOrEmpty(SelectedCustomHostProviderId))
                {
                    config.Host.CustomTransportSettings ??= new Dictionary<string, Dictionary<string, string>>();
                    config.Host.CustomTransportSettings[SelectedCustomHostProviderId] = ActiveHostSettings;
                }
            }
            else
            {
                config.Client.CustomTransportId = SelectedCustomJoinProviderId;
                if (!string.IsNullOrEmpty(SelectedCustomJoinProviderId))
                {
                    config.Client.CustomTransportSettings ??= new Dictionary<string, Dictionary<string, string>>();
                    config.Client.CustomTransportSettings[SelectedCustomJoinProviderId] = ActiveJoinSettings;
                }
            }

            config.Save();
        }

        private void StartHostingCustomGame()
        {
            using var _ = Profiler.Scope();

            var provider = TransportRegistry.GetExternalProvider(SelectedCustomHostProviderId);
            if (provider == null)
            {
                ShowCustomTransportError("No custom transport selected.");
                return;
            }

            if (!CanStartHosting())
            {
                ShowCustomTransportError(string.IsNullOrWhiteSpace(provider.LoginMessage)
                    ? "The custom transport is not ready yet."
                    : provider.LoginMessage);
                return;
            }

            string displayName = Configuration.Instance.Host.LanSettings.DisplayName;
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = Utils.GetLocalPlayerName();
            Configuration.Instance.Host.LanSettings.DisplayName = displayName;

            var settings = new Dictionary<string, string>(ActiveHostSettings);
            settings["transportId"] = provider.Id;
            settings["displayName"] = displayName;
            settings["maxPlayers"] = Configuration.Instance.Host.MaxLobbySize.ToString();
            settings["isHost"] = "1";

            if (!ValidateSettings(provider, settings, host: true))
                return;

            SaveCustomSettings(true);
            if (!NetworkConfig.PrepareCustomServer(provider.Id, settings))
            {
                ShowCustomTransportError("Failed to prepare the custom transport.");
                return;
            }

            if (Utils.IsInGame())
            {
                NetworkConfig.StartServer();
            }
            else
            {
                MultiplayerSession.ShouldHostAfterLoad = true;

                string saveForCurrentDlc = SaveLoader.GetLatestSaveForCurrentDLC();
                bool hasVersionCompatibleSave = !string.IsNullOrEmpty(saveForCurrentDlc)
                    && File.Exists(saveForCurrentDlc);

                if (hasVersionCompatibleSave)
                {
                    DebugConsole.Log("[UnityMultiplayerScreen/StartHostingCustomGame] Loading existing save.");
                    MainMenu.Instance?.LoadGame();
                    RegisterOnExitLoadScreenTriggers();
                }
                else
                {
                    DebugConsole.Log("[UnityMultiplayerScreen/StartHostingCustomGame] Starting new game.");
                    MainMenu.Instance?.NewGame();
                }
            }

            Show(false);
        }

        private void JoinCustomGame()
        {
            using var _ = Profiler.Scope();

            var provider = TransportRegistry.GetExternalProvider(SelectedCustomJoinProviderId);
            if (provider == null)
            {
                ShowCustomTransportError("No custom transport selected.");
                return;
            }

            if (!CanJoinCustom())
            {
                ShowCustomTransportError(string.IsNullOrWhiteSpace(provider.LoginMessage)
                    ? "The custom transport is not ready yet."
                    : provider.LoginMessage);
                return;
            }

            string displayName = Configuration.Instance.Host.LanSettings.DisplayName;
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = Utils.GetLocalPlayerName();
            Configuration.Instance.Host.LanSettings.DisplayName = displayName;

            var settings = new Dictionary<string, string>(ActiveJoinSettings);
            settings["transportId"] = provider.Id;
            settings["displayName"] = displayName;
            settings["isHost"] = "0";

            if (!ValidateSettings(provider, settings, host: false))
                return;

            SaveCustomSettings(false);
            if (!NetworkConfig.ConnectCustom(provider.Id, settings))
            {
                ShowCustomTransportError("Failed to start the custom transport.");
                return;
            }
            Show(false);
        }

        private bool ValidateSettings(ExternalTransportProvider provider, Dictionary<string, string> values, bool host)
        {
            foreach (var setting in provider.GetSettings())
            {
                if (!SettingApplies(setting.Scope, host))
                    continue;

                values.TryGetValue(setting.Key, out var value);

                if (setting.Required && string.IsNullOrWhiteSpace(value))
                {
                    ShowCustomTransportError($"Please fill in '{setting.Label}'.");
                    return false;
                }

                if (setting.Kind == ExternalTransportSettingKind.Integer
                    && !string.IsNullOrWhiteSpace(value)
                    && !int.TryParse(value, out _))
                {
                    ShowCustomTransportError($"'{setting.Label}' must be a number.");
                    return false;
                }
            }

            return true;
        }

        private static void ShowCustomTransportError(string message)
        {
            // Use a normal ONI dialog instead of MultiplayerOverlay, which replaces the
            // whole screen with the loading overlay.
            DialogUtil.CreateConfirmDialogFrontend("Custom Transport", message);
        }
    }
}
