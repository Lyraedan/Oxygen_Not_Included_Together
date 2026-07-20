using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Components;
using System;
using System.Collections.Generic;
using Shared.Profiling;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Utils = ONI_Together.Misc.Utils;

namespace ONI_Together.UI
{
	public class ChatScreen : KScreen
	{
		public TMP_InputField inputField;
		private RectTransform messageContainer;
		private Dictionary<long, TextMeshProUGUI> messages = new Dictionary<long, TextMeshProUGUI>();
		private RectTransform panelRectTransform;

		public static ChatScreen Instance;

		private GameObject header;
		private GameObject chatbox;
		private GameObject resizeHandles;
		private RectTransform chatWindowRootRT;
		private bool expanded = false;

		public struct PendingMessage
		{
			public long timestamp;
			public string message;
		}

		private static List<PendingMessage> pendingMessages = new List<PendingMessage>();

		public static void Show()
		{
			using var _ = Profiler.Scope();

			if (Instance != null)
				return;

			var go = new GameObject("ChatScreen", typeof(RectTransform));
			Instance = go.AddComponent<ChatScreen>();
			var parent = GameScreenManager.Instance.ssOverlayCanvas.transform;
			go.transform.SetParent(parent, false);

			var rt = go.GetComponent<RectTransform>();
			rt.anchorMin = new Vector2(0.5f, 0.5f);
			rt.anchorMax = new Vector2(0.5f, 0.5f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.sizeDelta = Vector2.zero;
			rt.anchoredPosition = new Vector2(0, 0);

			Instance.SetupUI();
		}
        private void SetupUI()
        {
	        using var _ = Profiler.Scope();

            var chatWindowRoot = new GameObject("ChatWindowRoot", typeof(RectTransform));
            chatWindowRoot.transform.SetParent(transform, false);
            var rootRT = chatWindowRoot.GetComponent<RectTransform>();
            chatWindowRootRT = rootRT;
            rootRT.anchorMin = new Vector2(0.5f, 0);
            rootRT.anchorMax = new Vector2(0.5f, 0);
            rootRT.pivot = new Vector2(0.5f, 0);

            var config = Configuration.Instance.Client;
            rootRT.sizeDelta = new Vector2(config.ChatWidth, config.ChatHeight);
            if (config.ChatWindowPositionSaved)
            {
                rootRT.anchoredPosition = new Vector2(config.ChatPositionX, config.ChatPositionY);
            }
            else
            {
                var scaler = GameScreenManager.Instance.ssOverlayCanvas.GetComponent<KCanvasScaler>();
                float scale = scaler != null ? scaler.GetCanvasScale() : 1f;
                float canvasW = Screen.width / scale;
                float canvasH = Screen.height / scale;
                float startX = canvasW * 0.5f - rootRT.rect.width * (1f - rootRT.pivot.x) - 20f;
                float startY = -canvasH * 0.5f + rootRT.rect.height * rootRT.pivot.y + 130f;
                rootRT.anchoredPosition = new Vector2(startX, startY);
            }

            header = new GameObject("ChatHeader", typeof(RectTransform), typeof(Image), typeof(Button));
            header.transform.SetParent(chatWindowRoot.transform, false);
            var headerRT = header.GetComponent<RectTransform>();
            headerRT.anchorMin = new Vector2(0, 1);
            headerRT.anchorMax = new Vector2(1, 1);
            headerRT.pivot = new Vector2(0.5f, 1);
            headerRT.anchoredPosition = new Vector2(0, 0);
            headerRT.sizeDelta = new Vector2(0, 30);
            header.GetComponent<Image>().color = new Color(0.4f, 0.2f, 0.3f, 0.9f);

            var drag = header.AddComponent<UIDragHandler>();
            drag.target = rootRT;
            drag.OnDragEnded += SaveChatWindowState;

            var chatboxContents = new GameObject("Chatbox_Contents");
            chatboxContents.transform.SetParent(chatWindowRoot.transform, false);
            chatbox = chatboxContents;

            var contentsRT = chatboxContents.AddComponent<RectTransform>();
            contentsRT.anchorMin = new Vector2(0, 0);
            contentsRT.anchorMax = new Vector2(1, 1);
            contentsRT.pivot = new Vector2(0.5f, 0);
            contentsRT.offsetMin = new Vector2(0, 0);
            contentsRT.offsetMax = new Vector2(0, -30);

            var panel = new GameObject("ChatPanel", typeof(Image));
            panel.transform.SetParent(chatboxContents.transform, false);
            panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.7f);
            panelRectTransform = panel.GetComponent<RectTransform>();
            panelRectTransform.anchorMin = Vector2.zero;
            panelRectTransform.anchorMax = Vector2.one;
            panelRectTransform.offsetMin = Vector2.zero;
            panelRectTransform.offsetMax = Vector2.zero;

            var scroll = CreateScrollArea("Scroll", panel.transform, out messageContainer);
            var scrollRT = scroll.GetComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0, 0);
            scrollRT.anchorMax = new Vector2(1, 1);
            scrollRT.pivot = new Vector2(0.5f, 1f);
            scrollRT.offsetMin = new Vector2(10, 50);
            scrollRT.offsetMax = new Vector2(-10, -10);

            messageContainer.anchorMin = new Vector2(0, 0);
            messageContainer.anchorMax = new Vector2(1, 1);
            messageContainer.offsetMin = Vector2.zero;
            messageContainer.offsetMax = Vector2.zero;
            messageContainer.pivot = new Vector2(0.5f, 1f);

            inputField = CreateInputField("ChatInput", panel.transform, new Vector2(10, 15), new Vector2(380, 30));
            var inputRT = inputField.GetComponent<RectTransform>();
            inputRT.anchorMax = new Vector2(1, 0);
            inputRT.offsetMin = new Vector2(10, 15);
            inputRT.offsetMax = new Vector2(-10, 45);
            inputField.onEndEdit.AddListener(OnInputSubmitted);

            var headerTextGO = new GameObject("HeaderText", typeof(TextMeshProUGUI));
            headerTextGO.transform.SetParent(header.transform, false);
            var headerText = headerTextGO.GetComponent<TextMeshProUGUI>();
            headerText.text = STRINGS.UI.MP_CHATWINDOW.RESIZE.RETRACT;
            headerText.alignment = TextAlignmentOptions.MidlineLeft;
            headerText.fontSize = 20;
            headerText.color = Color.white;
            headerText.margin = new Vector4(10, 0, 0, 0);

            var headerTextRT = headerText.GetComponent<RectTransform>();
            headerTextRT.anchorMin = new Vector2(0, 0);
            headerTextRT.anchorMax = new Vector2(1, 1);
            headerTextRT.offsetMin = new Vector2(5, 0);
            headerTextRT.offsetMax = new Vector2(-5, 0);

            expanded = Configuration.Instance.Client.ChatWindowExpanded;
            chatbox.SetActive(expanded);
            headerText.text = expanded ? STRINGS.UI.MP_CHATWINDOW.RESIZE.RETRACT : STRINGS.UI.MP_CHATWINDOW.RESIZE.EXPAND;
            header.GetComponent<Button>().onClick.AddListener(() =>
            {
                if (drag.WasDragged)
                    return;

                expanded = !expanded;
                chatbox.SetActive(expanded);
                headerText.text = expanded ? STRINGS.UI.MP_CHATWINDOW.RESIZE.RETRACT : STRINGS.UI.MP_CHATWINDOW.RESIZE.EXPAND;

                var config = Configuration.Instance.Client;
                config.ChatWindowExpanded = expanded;
                Configuration.Instance.Save();
            });

            header.transform.SetAsLastSibling();

            resizeHandles = new GameObject("ResizeHandles", typeof(RectTransform));
            resizeHandles.transform.SetParent(chatWindowRoot.transform, false);
            var resizeRT = resizeHandles.GetComponent<RectTransform>();
            resizeRT.anchorMin = Vector2.zero;
            resizeRT.anchorMax = Vector2.one;
            resizeRT.offsetMin = Vector2.zero;
            resizeRT.offsetMax = Vector2.zero;
            CreateResizeHandles(resizeHandles.transform, rootRT);

            AddSystemMessage(STRINGS.UI.MP_CHATWINDOW.CHAT_INITIALIZED);
            ProcessMessageQueue();

            StartCoroutine(FixInputFieldDisplay());
        }

        private void OnDestroy()
        {
            SaveChatWindowState();
        }

        private void SaveChatWindowState()
        {
            if (chatWindowRootRT == null) return;

            var config = Configuration.Instance.Client;
            config.ChatPositionX = chatWindowRootRT.anchoredPosition.x;
            config.ChatPositionY = chatWindowRootRT.anchoredPosition.y;
            config.ChatWidth = chatWindowRootRT.sizeDelta.x;
            config.ChatHeight = chatWindowRootRT.sizeDelta.y;
            config.ChatWindowPositionSaved = true;
            config.ChatWindowExpanded = expanded;
            Configuration.Instance.Save();
        }

        private void ResetChatWindowState()
        {
            if (chatWindowRootRT == null) return;

            chatWindowRootRT.sizeDelta = new Vector2(400, 230);

            var scaler = GameScreenManager.Instance.ssOverlayCanvas.GetComponent<KCanvasScaler>();
            float scale = scaler != null ? scaler.GetCanvasScale() : 1f;
            float canvasW = Screen.width / scale;
            float canvasH = Screen.height / scale;
            float startX = canvasW * 0.5f - chatWindowRootRT.rect.width * (1f - chatWindowRootRT.pivot.x) - 20f;
            float startY = -canvasH * 0.5f + chatWindowRootRT.rect.height * chatWindowRootRT.pivot.y + 130f;
            chatWindowRootRT.anchoredPosition = new Vector2(startX, startY);

            expanded = true;
            chatbox.SetActive(true);
            var headerText = header.GetComponentInChildren<TextMeshProUGUI>();
            if (headerText != null)
                headerText.text = STRINGS.UI.MP_CHATWINDOW.RESIZE.RETRACT;

            var config = Configuration.Instance.Client;
            config.ChatWindowPositionSaved = false;
            config.ChatPositionX = 0;
            config.ChatPositionY = 250;
            config.ChatWidth = 400;
            config.ChatHeight = 230;
            config.ChatWindowExpanded = true;
            Configuration.Instance.Save();
        }

        public void ProcessMessageQueue()
		{
			using var _ = Profiler.Scope();

			foreach (var pending in pendingMessages)
				QueueMessage(pending);
			pendingMessages.Clear();
		}

		public static void QueueMessage(PendingMessage message)
		{
			using var _ = Profiler.Scope();

			QueueMessage(message.timestamp, message.message);
		}

		private static void QueueMessage(long timestamp, string msg)
		{
			using var _ = Profiler.Scope();

			if (string.IsNullOrEmpty(msg))
			{
				return;
			}

			if (Instance != null)
				Instance.AddMessage(timestamp, msg);
			else
			{
				PendingMessage pending = new PendingMessage()
				{
					timestamp = timestamp,
					message = msg
				};
				pendingMessages.Add(pending);
			}
		}

		private System.Collections.IEnumerator FixInputFieldDisplay()
		{
			using var _ = Profiler.Scope();

			// Fixes the Caret and the text highlighting because without this they don't show
			yield return new WaitForEndOfFrame();
			inputField.gameObject.SetActive(false);
			yield return new WaitForEndOfFrame();
			inputField.gameObject.SetActive(true);
		}

		public void ClearMessages()
		{
			using var _ = Profiler.Scope();

			foreach (var tmp in messages.Values)
				Destroy(tmp.gameObject);
			messages.Clear();
		}

		public void AddMessage(long timestamp, string text)
		{
			using var _ = Profiler.Scope();

			DebugConsole.Log($"Adding chat message with timestamp {timestamp} : {text}");
			// We have a message with this timestamp already. Skip (Handles recieveing multiple of the same chat packet)
			if (messages.ContainsKey(timestamp))
				return;

			if (messageContainer == null)
			{
				DebugConsole.LogWarning("[Chatbox] Tried to add a message but messageContainer was null!");
				return;
			}

            // Create a new message object
            var go = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI));
			go.transform.SetParent(messageContainer, false);

			var rt = go.GetComponent<RectTransform>();
			rt.anchorMin = new Vector2(0, 1);
			rt.anchorMax = new Vector2(1, 1);
			rt.pivot = new Vector2(0, 1);
			rt.offsetMin = new Vector2(0, 0);
			rt.offsetMax = new Vector2(0, 0);

			var tmp = go.GetComponent<TextMeshProUGUI>();
			tmp.text = text;
			tmp.font = Utils.GetDefaultTMPFont();
			tmp.fontSize = 18;
            tmp.textWrappingMode = TextWrappingModes.Normal;
			tmp.richText = true;
			tmp.alignment = TextAlignmentOptions.TopLeft;
			tmp.color = new Color(0.9f, 0.9f, 0.9f, 1f);
			tmp.margin = new Vector4(6, 2, 6, 2);

			// Fit content height
			var fitter = go.AddComponent<ContentSizeFitter>();
			fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            messages.Add(timestamp, tmp);

			// Manually rebuild layout and force scroll to bottom
			LayoutRebuilder.ForceRebuildLayoutImmediate(messageContainer);
			var scrollRect = messageContainer.GetComponentInParent<ScrollRect>();
			if (scrollRect != null)
				scrollRect.verticalNormalizedPosition = 0f;
		}
		public static bool IsFocused()
		{
			using var _ = Profiler.Scope();

			return Instance != null && Instance.inputField != null && Instance.inputField.isFocused;
		}

		private void Update()
		{
			using var _ = Profiler.Scope();

			header.SetActive(MultiplayerSession.InActiveSession);
			chatbox.SetActive(MultiplayerSession.InActiveSession && expanded);
			resizeHandles.SetActive(MultiplayerSession.InActiveSession && expanded);
			if (!MultiplayerSession.InActiveSession)
			{
				return;
			}

			// ENTER while NOT focused: activate input
			else if (!inputField.isFocused && Input.GetKeyDown(KeyCode.Return))
			{
				inputField.ActivateInputField();
			}

			// ESC while focused: cancel typing
			else if (inputField.isFocused && Input.GetKeyDown(KeyCode.Escape))
			{
				inputField.DeactivateInputField();
			}

			// Shift+R: reset chat window position and size to defaults
			if (!inputField.isFocused && Input.GetKeyDown(KeyCode.R) &&
			    (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
			{
				ResetChatWindowState();
			}
		}

		public GameObject CreatePanel(string name, Transform parent, Vector2 size)
		{
			using var _ = Profiler.Scope();

			var panel = new GameObject(name, typeof(Image));
			panel.transform.SetParent(parent, false);

			var rt = panel.GetComponent<RectTransform>();
			rt.sizeDelta = size;
			rt.anchorMin = new Vector2(0.5f, 0); // Bottom center of screen
			rt.anchorMax = new Vector2(0.5f, 0);
			rt.pivot = new Vector2(0.5f, 0);
			rt.anchoredPosition = new Vector2(0, 20); // 20 pixels above bottom edge

			return panel;
		}

		public TMP_InputField CreateInputField(string name, Transform parent, Vector2 position, Vector2 size)
		{
			using var _ = Profiler.Scope();

			// Container with background
			var go = new GameObject(name, typeof(Image), typeof(TMP_InputField));
			go.transform.SetParent(parent, false);

			var rt = go.GetComponent<RectTransform>();
			rt.sizeDelta = size;
			rt.anchorMin = new Vector2(0, 0); // bottom left
			rt.anchorMax = new Vector2(0, 0);
			rt.pivot = new Vector2(0, 0);
			rt.anchoredPosition = position;

			var bg = go.GetComponent<Image>();
			bg.color = new Color(0.2f, 0.2f, 0.2f, 1f); // Background color

			var input = go.GetComponent<TMP_InputField>();
			input.lineType = TMP_InputField.LineType.SingleLine;
			input.scrollSensitivity = 0f;
			input.characterLimit = OxySyncChat.MAX_MESSAGE_LENGTH;

			// Viewport
			var viewportGO = new GameObject("TextViewport", typeof(RectMask2D), typeof(Image));
			viewportGO.transform.SetParent(go.transform, false);
			var viewportRT = viewportGO.GetComponent<RectTransform>();
			viewportRT.anchorMin = Vector2.zero;
			viewportRT.anchorMax = Vector2.one;
			viewportRT.offsetMin = new Vector2(5, 4);
			viewportRT.offsetMax = new Vector2(-5, -4);
			viewportGO.GetComponent<Image>().color = Color.clear;

			// Text component
			var textGO = new GameObject("Text", typeof(TextMeshProUGUI));
			textGO.transform.SetParent(viewportGO.transform, false);
			var tmp = textGO.GetComponent<TextMeshProUGUI>();
			tmp.fontSize = 18;
			tmp.textWrappingMode = TextWrappingModes.Normal;
			tmp.alignment = TextAlignmentOptions.MidlineLeft;
			tmp.color = Color.white;

			var textRT = tmp.GetComponent<RectTransform>();
			textRT.anchorMin = Vector2.zero;
			textRT.anchorMax = Vector2.one;
			textRT.offsetMin = Vector2.zero;
			textRT.offsetMax = Vector2.zero;

			// Highlight + Caret visuals
			input.textViewport = viewportRT;
			input.textComponent = tmp;
			input.placeholder = null;
			input.targetGraphic = bg;
			input.caretColor = Color.white;
			input.selectionColor = new Color(0.3f, 0.6f, 1f, 0.5f); // Light blue selection
			input.customCaretColor = true;

			return input;
		}

		public GameObject CreateScrollArea(string name, Transform parent, out RectTransform content)
		{
			using var _ = Profiler.Scope();

			var scrollGO = new GameObject(name, typeof(RectTransform), typeof(ScrollRect));
			scrollGO.transform.SetParent(parent, false);
			var scrollRT = scrollGO.GetComponent<RectTransform>();

			scrollRT.anchorMin = new Vector2(0, 0);
			scrollRT.anchorMax = new Vector2(1, 1);
			scrollRT.pivot = new Vector2(0.5f, 1);
			scrollRT.offsetMin = new Vector2(10, 50);
			scrollRT.offsetMax = new Vector2(-10, -10);

			// Viewport
			var viewport = new GameObject("Viewport", typeof(RectMask2D), typeof(Image));
			viewport.transform.SetParent(scrollGO.transform, false);
			var viewportRT = viewport.GetComponent<RectTransform>();
			viewportRT.anchorMin = Vector2.zero;
			viewportRT.anchorMax = Vector2.one;
			viewportRT.offsetMin = Vector2.zero;
			viewportRT.offsetMax = Vector2.zero;
			viewport.GetComponent<Image>().color = Color.clear;

			// Content container
			var contentGO = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
			contentGO.transform.SetParent(viewport.transform, false);
			content = contentGO.GetComponent<RectTransform>();

			var layout = contentGO.GetComponent<VerticalLayoutGroup>();
			layout.childAlignment = TextAnchor.UpperLeft;
			layout.childControlHeight = true;
			layout.childForceExpandHeight = false;
			layout.childControlWidth = true;
			layout.childForceExpandWidth = true;
			layout.spacing = 5;

			var fitter = contentGO.GetComponent<ContentSizeFitter>();
			fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

			var scrollbarGO = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
			scrollbarGO.transform.SetParent(scrollGO.transform, false);
			var scrollbarRT = scrollbarGO.GetComponent<RectTransform>();
			scrollbarRT.anchorMin = new Vector2(1, 0);
			scrollbarRT.anchorMax = Vector2.one;
			scrollbarRT.pivot = new Vector2(1, 1);
			scrollbarRT.offsetMin = new Vector2(-8, 0);
			scrollbarRT.offsetMax = Vector2.zero;
			scrollbarGO.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 0.5f);

			var handleGO = new GameObject("Handle", typeof(RectTransform), typeof(Image));
			handleGO.transform.SetParent(scrollbarGO.transform, false);
			var handleRT = handleGO.GetComponent<RectTransform>();
			handleRT.anchorMin = Vector2.zero;
			handleRT.anchorMax = Vector2.one;
			handleRT.offsetMin = new Vector2(2, 2);
			handleRT.offsetMax = new Vector2(-2, -2);
			handleGO.GetComponent<Image>().color = new Color(0.6f, 0.6f, 0.6f, 0.6f);

			var scrollbar = scrollbarGO.GetComponent<Scrollbar>();
			scrollbar.handleRect = handleRT;
			scrollbar.direction = Scrollbar.Direction.BottomToTop;
			scrollbar.targetGraphic = handleGO.GetComponent<Image>();

			viewportRT.offsetMax = new Vector2(-10, 0);

			var scroll = scrollGO.GetComponent<ScrollRect>();
			scroll.content = content;
			scroll.viewport = viewportRT;
			scroll.horizontal = false;
			scroll.vertical = true;
			scroll.movementType = ScrollRect.MovementType.Clamped;
			scroll.scrollSensitivity = 50f;
			scroll.verticalScrollbar = scrollbar;
			scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

			// Add layout group and fitter to viewport (optional but can help)
			viewport.AddComponent<CanvasRenderer>();

			return scrollGO;
		}

		public static bool IsMouseOverChatPanel()
		{
			using var _ = Profiler.Scope();

			if (Instance == null || Instance.panelRectTransform == null)
				return false;

			Vector2 mousePos = Input.mousePosition;
			return RectTransformUtility.RectangleContainsScreenPoint(Instance.panelRectTransform, mousePos, null);
		}

		private void OnInputSubmitted(string text)
		{
			using var _ = Profiler.Scope();

			if (!Input.GetKeyDown(KeyCode.Return)) return;

			if (!string.IsNullOrWhiteSpace(text))
			{
				OxySyncChat.Instance?.SendMessage(text);
				inputField.text = "";
			}

			inputField.DeactivateInputField();
		}

		public static void AddSystemMessage(string message)
		{
			QueueMessage(GeneratePendingMessage(message));
		}

		public static PendingMessage GeneratePendingMessage(string message)
		{
			using var _ = Profiler.Scope();

			PendingMessage pendingMessage = new PendingMessage()
			{
				timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				message = message
			};
			return pendingMessage;
        }
        private void CreateResizeHandles(Transform parent, RectTransform target)
		{
			using var _ = Profiler.Scope();

			float halfThick = 4f;
			float cornerSize = 12f;

			CreateHandle(parent, target, ResizeEdge.Top,
				new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
				new Vector2(0, -halfThick), new Vector2(0, halfThick));
			CreateHandle(parent, target, ResizeEdge.Bottom,
				new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f),
				new Vector2(0, -halfThick), new Vector2(0, halfThick));
			CreateHandle(parent, target, ResizeEdge.Left,
				new Vector2(0, 0), new Vector2(0, 1), new Vector2(0.5f, 0.5f),
				new Vector2(-halfThick, 0), new Vector2(halfThick, 0));
			CreateHandle(parent, target, ResizeEdge.Right,
				new Vector2(1, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
				new Vector2(-halfThick, 0), new Vector2(halfThick, 0));

			float halfCorner = cornerSize * 0.5f;
			CreateHandle(parent, target, ResizeEdge.Top | ResizeEdge.Left,
				new Vector2(0, 1), new Vector2(0, 1), new Vector2(0.5f, 0.5f),
				new Vector2(-halfCorner, -halfCorner), new Vector2(halfCorner, halfCorner));
			CreateHandle(parent, target, ResizeEdge.Top | ResizeEdge.Right,
				new Vector2(1, 1), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
				new Vector2(-halfCorner, -halfCorner), new Vector2(halfCorner, halfCorner));
			CreateHandle(parent, target, ResizeEdge.Bottom | ResizeEdge.Left,
				new Vector2(0, 0), new Vector2(0, 0), new Vector2(0.5f, 0.5f),
				new Vector2(-halfCorner, -halfCorner), new Vector2(halfCorner, halfCorner));
			CreateHandle(parent, target, ResizeEdge.Bottom | ResizeEdge.Right,
				new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f),
				new Vector2(-halfCorner, -halfCorner), new Vector2(halfCorner, halfCorner));
		}

		private void CreateHandle(Transform parent, RectTransform target, ResizeEdge edges,
			Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
			Vector2 offsetMin, Vector2 offsetMax)
		{
			var go = new GameObject($"ResizeHandle_{edges}", typeof(RectTransform), typeof(Image));
			go.transform.SetParent(parent, false);

			var rt = go.GetComponent<RectTransform>();
			rt.anchorMin = anchorMin;
			rt.anchorMax = anchorMax;
			rt.pivot = pivot;
			rt.offsetMin = offsetMin;
			rt.offsetMax = offsetMax;

			var img = go.GetComponent<Image>();
			img.color = Color.clear;

            var handle = go.AddComponent<UIResizeHandle>();
			handle.target = target;
			handle.edges = edges;
			handle.OnDragEnded += SaveChatWindowState;
		}

        public void Clamp(Vector2 position, RectTransform target)
		{
			Vector2 newPos = position;

            RectTransform canvasRect = GameScreenManager.Instance.ssOverlayCanvas.GetComponent<RectTransform>();

            float padding = 10f;
            float leftLimit = -canvasRect.rect.width * 0.5f + target.rect.width * target.pivot.x + padding;
            float rightLimit = canvasRect.rect.width * 0.5f - target.rect.width * (1f - target.pivot.x) - padding;
            float bottomLimit = -canvasRect.rect.height * 0.5f + target.rect.height * target.pivot.y + padding;
            float topLimit = canvasRect.rect.height * 0.5f - target.rect.height * (1f - target.pivot.y) - padding;

            newPos.x = Mathf.Clamp(newPos.x, leftLimit, rightLimit);
            newPos.y = Mathf.Clamp(newPos.y, bottomLimit, topLimit);

            target.anchoredPosition = newPos;
        }
	}
}
