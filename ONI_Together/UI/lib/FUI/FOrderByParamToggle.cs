using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UI.lib.UIcmp;
using ONI_Together.UI.lib;

namespace UI.lib.UI.FUI
{
	public class FOrderByParamToggle : KMonoBehaviour
	{
		[SerializeField] public string Label;
		[SerializeField] public bool StartDescending;

		[SerializeField] public Image Default, Ascending, Descending, CompactIcon;
		[SerializeField] public FButton Toggle;
		[SerializeField] public float CompactSwitchSize = -1;
		[SerializeField] public Sprite CompactIconSprite = null;
		[SerializeField] public LocText Text = null;

		int state = 0;
		public System.Action OnAscending, OnDescending;
		bool init = false;
		Rect lastSize;
		RectTransform rectTransform;

		void Init()
		{
			if (init)
				return;
			init = true;

			Default = transform.Find("SortButton/IconInactive").gameObject.GetComponent<Image>();
			Ascending = transform.Find("SortButton/IconAscending").gameObject.GetComponent<Image>();
			Descending = transform.Find("SortButton/IconDescending").gameObject.GetComponent<Image>();
			CompactIcon = transform.Find("SmallSizeIcon")?.gameObject?.GetComponent<Image>();
			Toggle = transform.Find("SortButton").gameObject.AddOrGet<FButton>();
			Toggle.ClearOnClick();
			Toggle.OnClick += OnToggleClicked;

			SetText();
			UpdateImages();
		}

		public override void OnPrefabInit()
		{
			Init();
			rectTransform = transform.rectTransform();
			base.OnPrefabInit();
		}
		public override void OnSpawn()
		{
			base.OnSpawn();
			Invoke("OnResize", 0.01f);
			SetText();
		}
		void SetText()
		{
			if (!Label.IsNullOrWhiteSpace())
			{
				Text = transform.GetComponentInChildren<LocText>();
				Text?.SetText(Label);
				if (CompactIcon != null)
					UIUtils.AddSimpleTooltipToObject(CompactIcon.gameObject, Label);
			}

		}

		public void SetActions(System.Action sortAscended, System.Action sortDescended)
		{
			Init();
			OnAscending = sortAscended;
			OnDescending = sortDescended;
		}
		public void SetCompactIcon(Sprite compactIcon, float compactSwitchWidth)
		{
			CompactIconSprite = compactIcon;
			CompactSwitchSize = compactSwitchWidth;
		}


		void OnToggleClicked()
		{
			state = state switch
			{
				0 => StartDescending ? 2 : 1,
				1 => 2,
				2 => 1,
				_ => 0
			};
			UpdateImages();
			if (state == 1)
				OnAscending?.Invoke();
			if (state == 2)
				OnDescending?.Invoke();
		}
		public void ActivateToggle(int stateOverride = -1)
		{
			Init();
			state = StartDescending ? 2 : 1;
			if (stateOverride >= 0)
				state = stateOverride;
			UpdateImages();
		}
		public void DeactivateToggle()
		{
			Init();
			state = 0;
			UpdateImages();
		}
		void UpdateImages()
		{
			Default.gameObject.SetActive(state == 0);
			Ascending.gameObject.SetActive(state == 1);
			Descending.gameObject.SetActive(state == 2);
		}


		void Update()
		{
			if (rectTransform.rect != lastSize)
			{
				OnResize();
			}
		}
		void OnResize()
		{
			lastSize = rectTransform.rect;
			if (Text == null || CompactIcon == null || CompactIconSprite == null || CompactSwitchSize < 0) return;
			bool useCompact = rectTransform.rect.width <= CompactSwitchSize;
			CompactIcon.sprite = CompactIconSprite;
			CompactIcon.gameObject.SetActive(useCompact);
			Text.gameObject.SetActive(!useCompact);
		}
	}
}
