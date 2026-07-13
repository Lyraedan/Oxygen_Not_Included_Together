using ONI_Together.Networking;
using ONI_Together.UI.Components;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TMPro;
using TUNING;
using UI.lib.UI.FUI;
using UI.lib.UIcmp;
using UnityEngine;
using UnityEngine.UI;
using static AnimEventHandler;
using static ONI_Together.STRINGS.UI.MP_CHATBOX.TOPBAR.FILTERBUTTON;
using ONI_Together.Networking.OxySync.Components;

namespace ONI_Together.UI
{
	internal class UnityChatBoxUI : KScreen//, IRender1000ms
	{

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

		public static void DestroyInstance() { Instance = null; }


		public static void InitScreen()
		{
			if (Instance == null)
			{
				Instance = Util.KInstantiateUI<UnityChatBoxUI>(ModAssets.MP_Chatbox, GameObject.Find("ScreenSpaceOverlayCanvas"), true);
				//under build menu, above notifications
				Instance.transform.SetSiblingIndex(4);
				Instance.Init();
			}
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

			ProcessPendingSystemMessages();
		}

		void SendChatMessage()
		{
			var messageText = MsgInput.Text;
			MsgInput.SetTextFromData(string.Empty);
			if (messageText.Any())
			{
				OxySyncChat.Instance?.SendMessage(messageText);
			}
		}

		public void SendNewNewMessage(string sender, string timestamp, string message)
		{

			//Debug.Log("ChatScroll.verticalNormalizedPosition: " + ChatScroll.verticalNormalizedPosition);
			//if its roughly at the bottom, automatically scroll down
			bool scrollDown = ChatScroll.verticalNormalizedPosition <= 0.01f || ChatScroll.verticalNormalizedPosition == 1;

			var newMessage = Util.KInstantiateUI<ChatMessageContainer>(MsgPrefab.gameObject, ItemContainer, true);
			newMessage.SetValues(sender, timestamp, message);
			ChatMessages.Add(newMessage);
			if (scrollDown && gameObject.activeInHierarchy)
				StartCoroutine(ScrollToBottom());
		}
		public static bool IsFocused()
		{
			return Instance != null && Instance.MsgInput?.isEditing == true;
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
				Instance.SendNewNewMessage("<color=yellow>System</color>", timestampString, message);
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
			return mouseOver ? 100 : base.GetSortKey();
		}


		void OnResized()
		{
			//TODO
			//ModAssets.Config.OnResized(transform.rectTransform());

			Canvas.ForceUpdateCanvases();
			LayoutRebuilder.ForceRebuildLayoutImmediate(ChatScroll.content);
		}
		void OnMoved()
		{
			var pos = transform.localPosition;
			var canvas = transform.GetComponentInParent<Canvas>().pixelRect;
			var scale = transform.GetComponentInParent<CanvasScaler>().scaleFactor;

			Debug.Log("ChatScreeon on Dragged pos: " + pos);

			float halfWidth = (canvas.width / 2f) / scale;
			float halfHeight = (canvas.height / 2f) / scale;

			var size = transform.rectTransform().sizeDelta / scale;

			float lowerBoundX = -halfWidth + size.x;
			float upperBoundX = halfWidth;
			float lowerBoundY = -halfHeight;
			float upperBoundY = halfHeight - size.y;


			pos.y = Mathf.Clamp(pos.y, lowerBoundY, upperBoundY);
			pos.x = Mathf.Clamp(pos.x, lowerBoundX, upperBoundX);
			transform.SetLocalPosition(pos);
			//ModAssets.Config.OnMoved(transform);
		}
		void OnResetSize(bool _)
		{
			transform.rectTransform().sizeDelta = new(302, 490);
			OnResized();
		}
		void OnResetPos(bool _)
		{
			transform.localPosition = (new(0, 0));
			//transform.localPosition = defaultPos;
			OnMoved();
		}

		void Refresh()
		{
			StartCoroutine(ScrollToBottom());
		}
		static string Now() => System.DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

		public override void Show(bool show = true)
		{
			base.Show(show);
			//	if (!show)
			//		return;
			//	SendNewNewMessage("Sender A", "19:22", "This is a textmessage test");
			//	SendNewNewMessage("Sender B", "19:23", "This is also a textmessage test");
			//	SendNewNewMessage("Sender A", "19:24", "when the imposter is sus");
			//	SendNewNewMessage("Sender C", "20:25", "Amogus");
			//	SendNewNewMessage("Podel", "22:22", "The Attack on pearl harbor was a surprise military strike on the United States Pacific Fleet at its naval base at Pearl Harbor on Oahu, Hawaii Territory. \r\n");
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
		}
	}
}
