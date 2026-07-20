using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.UI.Components;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Shared;
using TMPro;
using TUNING;
using UI.lib.UI.FUI;
using UI.lib.UIcmp;
using UnityEngine;
using UnityEngine.UI;
using static AnimEventHandler;
using static ONI_Together.STRINGS.UI.MP_CHATBOX.TOPBAR.FILTERBUTTON;
using static ONI_Together.STRINGS.UI.MP_CHATBOX.TOPBAR.FILTERBUTTON.DROPDOWNCONTENT;
using static STRINGS.UI.CLUSTERMAP;

namespace ONI_Together.UI
{
	internal class UnityChatBoxUI : KScreen//, IRender1000ms
	{
		public static void DestroyInstance()
		{
			Instance = null;
		}


		public static MultiToggle ChatToggle = null;
		public UnityChatBoxUI() : base()
		{
			ConsumeMouseScroll = true;
		}

		public static UnityChatBoxUI Instance = null;

		GameObject ItemContainer;

		ChatMessageContainer MsgPrefab;
		FButton Close;
		FMultiSelectDropdown Options;
		ScrollRect ChatScroll;
		FInputField2 MsgInput;
		FButton SendMsg;

		List<ChatMessageContainer> ChatMessages = [];
		static List<string> _pendingSystemMessages = [];

		public static void InitToggle()
		{
			ChatToggle?.onClick = () => OnToggleClicked();
			ChatToggle?.ChangeState(0);
		}

		static void OnToggleClicked()
		{
			if (!MultiplayerSession.InActiveSession)
			{
				KMonoBehaviour.PlaySound(GlobalAssets.GetSound("Negative"));
				ChatToggle.ChangeState(0);
				return;
			}
			else
			{
				InitScreen();
				Instance.Show(ChatToggle.state == 1);
			}
		}

		public static void InitScreen()
		{
			if (Instance == null)
			{
				Instance = Util.KInstantiateUI<UnityChatBoxUI>(ModAssets.MP_Chatbox, GameObject.Find("ScreenSpaceOverlayCanvas"), true);
				//under build menu, above notifications
				Instance.transform.SetSiblingIndex(4);
				Instance.Init();

				Game.Instance.Subscribe(MP_HASHES.OnInSessionChanged, OnSessionChanged);
				//Game.Instance.Subscribe(MP_HASHES.GameClient_OnConnectedInGame, ShowOnNewSession);
				//Game.Instance.Subscribe(MP_HASHES.GameServer_OnServerStarted, ShowOnNewSession);
				//Game.Instance.Subscribe(MP_HASHES.OnDisconnected, HideOnDisconnect);
				Instance.Show(false);
				ChatToggle?.ChangeState(0);
			}
		}

		private static void OnSessionChanged(object data)
		{
			InitScreen();
			//bool show = ((Boxed<bool>)data).value;
			Instance.Show(MultiplayerSession.InActiveSession);
		}

		static void ShowOnNewSession(object _)
		{
			InitScreen();
			Instance.Show(MultiplayerSession.InActiveSession);
		}
		static void HideOnDisconnect(object _)
		{
			if (Instance == null)
				return;
			if (!Utils.IsInGame())
				Instance.Show(false);
		}


		public override void OnShow(bool show)
		{
			base.OnShow(show);
			if (show)
			{
				//transform.SetAsLastSibling();
				Refresh();
				Debug.Log("Chatbox Loc position: " + transform.localPosition);
			}
			this.ConsumeMouseScroll = show;
		}
		public override void OnPrefabInit()
		{
			base.OnPrefabInit();
			Init();
		}

		bool init;
		void Init()
		{
			if (init)
				return;
			init = true;

			ItemContainer = transform.Find("ScrollArea/Content").gameObject;

			ChatScroll = transform.Find("ScrollArea").gameObject.GetComponent<ScrollRect>();
			ChatScroll.verticalNormalizedPosition = 0;
			Close = transform.Find("TopBar/CloseButton").gameObject.AddOrGet<FButton>();
			Close.OnClick += () => Show(false);

			MsgInput = transform.Find("TextInput/Input").gameObject.AddOrGet<FInputField2>();
			MsgInput.Text = string.Empty;
			MsgInput.OnSubmit.AddListener(_ => SendChatMessage());

			SendMsg = transform.Find("TextInput/SendButton").gameObject.AddOrGet<FButton>();
			SendMsg.OnClick += SendChatMessage;

			Options = transform.Find("TopBar/FilterButton").gameObject.AddOrGet<FMultiSelectDropdown>();
			Options.DropDownEntries = [
				new FMultiSelectDropdown.FDropDownButtonEntry(DROPDOWNCONTENT.RESETPOS.LABEL, OnResetPos),
				new FMultiSelectDropdown.FDropDownButtonEntry(DROPDOWNCONTENT.RESETSIZE.LABEL, OnResetSize)
				];
			Options.InitializeDropDown();

			var drag = transform.Find("TopBar").gameObject.AddOrGet<DraggablePanel>();
			drag.Target = transform;
			drag.OnDragged = OnMoved;

			var resize = transform.Find("ResizeKnob").gameObject.AddOrGet<ResizeDragKnob>();
			resize.Target = transform;
			resize.OnResized = OnResized;
			var cfg = Configuration.Instance.Client;
			//transform.rectTransform().sizeDelta = new(cfg.ChatWidth, cfg.ChatHeight);
			//transform.localPosition = new(cfg.ChatPositionX, cfg.ChatPositionY);

			MsgPrefab = transform.Find("ScrollArea/Content/MessagePrefab").gameObject.AddOrGet<ChatMessageContainer>();
			MsgPrefab.gameObject.SetActive(false);
			RestoreSavedPosition();
			ProcessPendingSystemMessages();
		}

		void SendChatMessage()
		{
			//Debug.Log("Chatbox Pos: "+transform.localPosition);

			var messageText = MsgInput.Text;
			MsgInput.SetTextFromData(string.Empty);
			if (messageText.Any())
			{
				OxySyncChat.Instance?.SendMessage(messageText);
			}
		}

		public void SendNewChatMessage(string sender, string timestamp, string message, Color borderColor = default)
		{
			//Debug.Log("ChatScroll.verticalNormalizedPosition: " + ChatScroll.verticalNormalizedPosition);
			//if its roughly at the bottom, automatically scroll down
			bool scrollDown = ChatScroll.verticalNormalizedPosition <= 0.01f || ChatScroll.verticalNormalizedPosition == 1;

			//on game quit, these are null
			if (MsgPrefab.IsNullOrDestroyed() || ItemContainer.IsNullOrDestroyed() || MsgPrefab.gameObject.IsNullOrDestroyed())
				return;

			var newMessage = Util.KInstantiateUI<ChatMessageContainer>(MsgPrefab.gameObject, ItemContainer, true);
			newMessage.SetValues(sender, timestamp, message, borderColor);
			ChatMessages.Add(newMessage);
			if (scrollDown)
				ScrollMessagesToEnd();
			else
				ForceUpdateUI();
		}
		public static bool IsFocused()
		{
			return Instance != null && Instance.MsgInput?.isEditing == true;
		}

		void RestoreSavedPosition()
		{
			var config = Configuration.Instance.Client;
			if (config.ChatWindowPositionSaved)
			{
				transform.localPosition = new(config.ChatPositionX, config.ChatPositionY);
				OnMoved();
			}
			else
				OnResetPos();

			if (config.ChatWindowDimensionsSaved)
			{
				transform.rectTransform().sizeDelta = new(config.ChatWidth, config.ChatHeight);
				OnResized();
			}
			else
				OnResetSize();
		}
		void ScrollMessagesToEnd()
		{
			if (gameObject.activeInHierarchy)
				StartCoroutine(ScrollToBottom());
		}

		public static bool IsMouseOverChatPanel()
		{
			return Instance != null && Instance.mouseOver;
		}

		public static void AddSystemMessage(string message)
		{
			if (Instance != null)
			{
				long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				string timestampString = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
				Instance.SendNewChatMessage("<color=yellow>System</color>", timestampString, message);
			}
			else
			{
				_pendingSystemMessages.Add(message);
			}
		}

		void ProcessPendingSystemMessages()
		{
			if (_pendingSystemMessages.Count == 0)
				return;
			var messages = new List<string>(_pendingSystemMessages);
			_pendingSystemMessages.Clear();
			foreach (var msg in messages)
				AddSystemMessage(msg);
		}

		private IEnumerator ScrollToBottom()
		{
			yield return null;

			Canvas.ForceUpdateCanvases();
			LayoutRebuilder.ForceRebuildLayoutImmediate(ChatScroll.content);

			ChatScroll.verticalNormalizedPosition = 0f;
		}
		public override float GetSortKey()
		{
			return mouseOver || MsgInput.isEditing ? 100 : base.GetSortKey();
		}


		void OnResized()
		{
			//TODO
			//ModAssets.Config.OnResized(transform.rectTransform());

			Canvas.ForceUpdateCanvases();
			LayoutRebuilder.ForceRebuildLayoutImmediate(ChatScroll.content);

			SaveChatWindowSize();
		}

		private IEnumerator DelayedForceLayout()
		{
			yield return null;
			Canvas.ForceUpdateCanvases();
			LayoutRebuilder.ForceRebuildLayoutImmediate(ChatScroll.content);
		}
		void ForceUpdateUI()
		{
			if (gameObject.activeInHierarchy)
				StartCoroutine(DelayedForceLayout());
		}

		void OnMoved()
		{
			var pos = transform.localPosition;
			var canvas = transform.GetComponentInParent<Canvas>().pixelRect;
			var scale = transform.GetComponentInParent<CanvasScaler>().scaleFactor;

			//Debug.Log("ChatScreeon on Dragged pos: " + pos);

			float halfWidth = (canvas.width / 2f) / scale;
			float halfHeight = (canvas.height / 2f) / scale;

			var size = transform.rectTransform().sizeDelta;

			float lowerBoundX = -halfWidth + size.x;
			float upperBoundX = halfWidth;
			float lowerBoundY = -halfHeight;
			float upperBoundY = halfHeight - size.y;


			pos.y = Mathf.Clamp(pos.y, lowerBoundY, upperBoundY);
			pos.x = Mathf.Clamp(pos.x, lowerBoundX, upperBoundX);
			transform.SetLocalPosition(pos);

			SaveChatWindowPos();
		}
		void OnResetSize(bool _ = false)
		{
			transform.rectTransform().sizeDelta = new(302, 490);
			OnResized();
		}
		void OnResetPos(bool _ = true)
		{
			var canvas = transform.GetComponentInParent<Canvas>().pixelRect;
			var scale = transform.GetComponentInParent<CanvasScaler>().scaleFactor;

			float halfWidth = (canvas.width / 2f) / scale;
			float halfHeight = (canvas.height / 2f) / scale;

			var size = transform.rectTransform().sizeDelta;

			float lowerBoundX = -halfWidth + size.x;
			float lowerBoundY = -halfHeight;

			transform.localPosition = new Vector2(lowerBoundX + 21, lowerBoundY + 101);
			//transform.localPosition = defaultPos;
			OnMoved();
		}

		void Refresh()
		{
			//ScrollMessagesToEnd();
		}
		static string Now() => System.DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

		public override void Show(bool show = true)
		{
			base.Show(show);
			if (show)
				StartCoroutine(ScrollToBottom());

			UpdateToggleStateOnShow(show);
		}
		void UpdateToggleStateOnShow(bool show)
		{
			int state = 0;
			if (show)
				state = 2;
			else if (MultiplayerSession.InActiveSession)
				state = 1;
			ChatToggle?.ChangeState(state);
		}

		private void SaveChatWindowSize()
		{
			var config = Configuration.Instance.Client;
			Vector2 size = transform.rectTransform().sizeDelta;
			config.ChatWidth = size.x;
			config.ChatHeight = size.y;
			config.ChatWindowDimensionsSaved = true;
			config.ChatWindowExpanded = true;
			Configuration.Instance.Save();
		}
		private void SaveChatWindowPos()
		{
			var config = Configuration.Instance.Client;
			config.ChatPositionX = transform.localPosition.x;
			config.ChatPositionY = transform.localPosition.y;
			config.ChatWindowPositionSaved = true;
			Configuration.Instance.Save();
		}

		public override void OnKeyDown(KButtonEvent e)
		{
			base.OnKeyDown(e);
			if (mouseOver)
			{
				if (e.TryConsume(Action.Escape) && MsgInput.isEditing)
				{
					MsgInput.ExternalStopEditing();
				}
			}
			if (e.TryConsume(Action.DialogSubmit) && !MsgInput.isEditing)
			{
				MsgInput.ExternalStartEditing();
			}
			e.TryConsume(Action.DialogSubmit);
			e.TryConsume(Action.Escape);
		}
		public static bool ChatActive => Instance.gameObject.activeSelf;

		internal static void AddLinkToChatInput(string linkText)
		{
			if (Instance == null || !ChatActive)
				return;
			Instance.MsgInput.Text += linkText;
		}
	}
}
