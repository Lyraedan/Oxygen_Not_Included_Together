using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UI.lib.UIcmp;

namespace UI.lib.UI.FUI
{
	public class FColorPickerArray : KMonoBehaviour
	{
		class FColorPickerEntry : KMonoBehaviour
		{
			Image highlight, bgColor;
			FButton button;
			Color TargetColor;
			Action<Color> OnColorSelect = null;

			public override void OnPrefabInit()
			{
				base.OnPrefabInit();
				highlight = GetComponent<Image>();
				bgColor = transform.Find("Image").gameObject.GetComponent<Image>();
				button = gameObject.AddOrGet<FButton>();
				button.OnClick += ColorEntryClicked;
				Refresh(Color.white);
			}
			public void Init(Color target, Action<Color> onColorSelect)
			{
				TargetColor = target;
				OnColorSelect = onColorSelect;
			}

			public void Refresh(Color selectedColor)
			{
				if (highlight == null)
					return;

				highlight.color = selectedColor == TargetColor ? Color.white : Color.black;
				if(bgColor.color != TargetColor)
					bgColor.color = TargetColor;
			}
			void ColorEntryClicked()
			{
				OnColorSelect?.Invoke(TargetColor);
			}
		}

		FColorPickerEntry Prefab;
		Dictionary<Color, FColorPickerEntry> PalletteEntries = [];
		public Color SelectedColor = Color.white;
		public event Action<Color> OnColorChange;
		public int Width = 240, PrefabWidth = 15;

		public void SetSelected(Color selectedColor)
		{
			OnSelectColor(selectedColor);
		}

		public override void OnPrefabInit()
		{
			base.OnPrefabInit();
			InitPallette();
		}
		public override void OnSpawn()
		{
			base.OnSpawn();
			RefreshPallette();
		}
		
		void InitPallette()
		{
			Prefab = transform.Find("Item").gameObject.AddComponent<FColorPickerEntry>();
			Prefab.gameObject.SetActive(false);

			for (int i = transform.childCount - 1; i >= 0; i--)
			{
				var child = transform.GetChild(i);
				if(child.gameObject != Prefab.gameObject)
					Destroy(child.gameObject);
			}
			
			List<Color> palletteColors = [
				Color.white,
				new(0.75f,0.75f,0.75f),
				Color.gray,
				new(0.25f,0.25f,0.25f),
				Color.black
				];

			var prefabRect = Prefab.rectTransform();
			var rect = this.rectTransform();

			float degreeSteps = Width / PrefabWidth - 1;//-1 for the greyscale selections

			for(float stepper = 0; stepper < degreeSteps; stepper++)
			{
				float hue = stepper / degreeSteps;

				palletteColors.Add(Color.HSVToRGB(hue, 0.33f, 1));
				palletteColors.Add(Color.HSVToRGB(hue, 0.66f, 1));
				palletteColors.Add(Color.HSVToRGB(hue, 1, 1));
				palletteColors.Add(Color.HSVToRGB(hue, 1, 0.66f));
				palletteColors.Add(Color.HSVToRGB(hue, 1, 0.33f));
			}

			foreach(var  color in palletteColors)
			{
				var entry = Util.KInstantiateUI<FColorPickerEntry>(Prefab.gameObject, gameObject);
				entry.Init(color, OnSelectColor);
				entry.gameObject.SetActive(true);
				PalletteEntries[color] = entry;
			}
		}
		void RefreshPallette()
		{
			foreach (var entry in PalletteEntries)
				entry.Value.Refresh(SelectedColor);
		}
		void OnSelectColor(Color color)
		{
			if(color != SelectedColor)
			{
				SelectedColor = color;
				OnColorChange?.Invoke(SelectedColor);
				RefreshPallette();
			}
		}
	}
}
