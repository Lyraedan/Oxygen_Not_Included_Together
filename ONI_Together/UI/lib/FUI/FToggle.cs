using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UI.lib.UIcmp //Source: Aki
{
	public class FToggle : KMonoBehaviour, IEventSystemHandler, IPointerDownHandler, IPointerEnterHandler
	{
		[SerializeField]
		public Image mark;

		public event System.Action<bool> OnClick;
		public event System.Action<bool> OnChange;

		private bool _interactable = true;

		public bool Interactable => _interactable;

		public void SetInteractable(bool interactable)
		{
			_interactable = interactable;
			if (mark != null)
			{
				mark.color = _interactable ? Color.white : new Color(1, 1, 1, 0.5f);
			}

		}

		private bool on;

		public bool On
		{
			get => on;
			set
			{
				on = value;
				if (mark != null && Interactable)
				{
					mark.enabled = value;
					OnChange?.Invoke(value);
				}
			}
		}

		public override void OnPrefabInit()
		{
			base.OnPrefabInit();

			if (mark == null)
				mark = gameObject.GetComponentInChildren<Image>();
		}

		public void SetCheckmark(string path)
		{
			mark = transform.Find(path).GetComponent<Image>();
		}

		public override void OnSpawn()
		{
			base.OnSpawn();
		}

		public void Toggle() => On = !On;
		public void SetOn(bool toggleOn) => On = toggleOn;

		public void SetOnFromCode(bool setOn)
		{
			on = setOn;
			if (mark != null)
			{
				mark.enabled = on;
			}
		}

		public void OnPointerDown(PointerEventData eventData)
		{
			if (KInputManager.isFocused && Interactable)
			{
				KInputManager.SetUserActive();
				PlaySound(UISoundHelper.Click);
				Toggle();
				OnClick?.Invoke(On);

			}
		}

		public void OnPointerEnter(PointerEventData eventData)
		{
			if (KInputManager.isFocused && Interactable)
			{
				KInputManager.SetUserActive();
				PlaySound(UISoundHelper.MouseOver);
			}
		}
	}
}
