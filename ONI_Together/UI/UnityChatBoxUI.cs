using ONI_Together.UI.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TUNING;
using UI.lib.UI.FUI;
using UI.lib.UIcmp;
using UnityEngine;
using UnityEngine.UI;
using static AnimEventHandler;
using static ONI_Together.STRINGS.UI.MP_CHATBOX.TOPBAR.FILTERBUTTON;

namespace ONI_Together.UI
{
	internal class UnityChatBoxUI : KScreen
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

		List<ChatMessageContainer> ChatMessages = [];

		public static void DestroyInstance() { Instance = null; }

		public static void InitScreen(GameObject parent)
		{
			if (Instance == null)
			{
				Instance = Util.KInstantiateUI<UnityChatBoxUI>(ModAssets.MP_Chatbox, parent, true);
				Instance.Init();
				Instance.Show(false);
			}
		}
		public override void OnShow(bool show)
		{
			base.OnShow(show);
			if (show)
			{
				//transform.SetAsLastSibling();
				Refresh();
			}
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
			

			Close = transform.Find("TopBar/CloseButton").gameObject.AddOrGet<FButton>();
			Close.OnClick += () => ManagementMenu.Instance.CloseAll();
			Close.PlayClickSound = false;

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
		}
		public void SendNewNewMessage(string sender, string timestamp, string message)
		{
			var newMessage = Util.KInstantiateUI<ChatMessageContainer>(MsgPrefab.gameObject, ItemContainer, true);
			newMessage.SetValues(sender, timestamp, message);
			ChatMessages.Add(newMessage);
		}


		void OnResized()
		{
			//TODO
			//ModAssets.Config.OnResized(transform.rectTransform());
		}
		void OnMoved()
		{
			var pos = transform.localPosition;
			var canvas = transform.GetComponentInParent<Canvas>().pixelRect;
			var scale = transform.GetComponentInParent<CanvasScaler>().scaleFactor;
			//pos.y = Mathf.Clamp(pos.y, -((canvas.height / scale) - 180), 60);
			//pos.x = Mathf.Clamp(pos.x, -((canvas.width / scale) - 150), 20);

			//transform.SetLocalPosition(pos);
			//ModAssets.Config.OnMoved(transform);
		}
		void OnResetSize(bool _)
		{
			transform.rectTransform().sizeDelta = new(302, 450);
			OnResized();
		}
		void OnResetPos(bool _)
		{
			transform.SetLocalPosition(new(-50, 50));
			OnMoved();
		}

		void Refresh()
		{
		}





		public override void Show(bool show = true)
		{
			base.Show(show);
		}
	}
}
