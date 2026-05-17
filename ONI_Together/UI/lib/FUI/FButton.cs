using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UI.lib.UIcmp //Source: Aki
{
	public class FButton : KMonoBehaviour, IEventSystemHandler, IPointerUpHandler, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler
	{
		public bool PlayClickSound = true;
		public event System.Action OnClick;
		public event System.Action OnRightClick;


		public event System.Action OnPointerEnterAction;
		public event System.Action OnPointerExitAction;

		private bool interactable;
		private Material material;

		[MyCmpGet]
		private Image image;

		[MyCmpGet]
#pragma warning disable IDE0044
		private Button button;
#pragma warning restore IDE0044

		[SerializeField]
		public Color disabledColor = new Color(0.78f, 0.78f, 0.78f);

		[SerializeField]
		public Color normalColor = new Color(0.243f, 0.263f, 0.341f);

		[SerializeField]
		public Color hoverColor = new Color(0.345f, 0.373f, 0.702f);

		public bool allowRightClick = false;
		public override void OnPrefabInit()
		{
			base.OnPrefabInit();
			if (button != null && button.image != null)
			{
				image = button.image;
			}

			material = image.material;
			interactable = true;
		}
		public override void OnSpawn()
		{
			base.OnSpawn();
			if (button != null)
				button.navigation = GetNoNavigation();
		}
		Navigation GetNoNavigation()
		{
			Navigation result = default(Navigation);
			result.mode = Navigation.Mode.None;
			result.wrapAround = false;
			return result;
		}
		public void SetInteractable(bool interactable)
		{
			if (this.IsNullOrDestroyed() || button == null && image == null)
				return;


			if (interactable == this.interactable)
			{
				return;
			}

			this.interactable = interactable;
			//image.material = interactable ? material : global::Assets.instance.UIPrefabAssets.TableScreenWidgets.DesaturatedUIMaterial;
			if (button == null)
			{
				if (image != null)
					image.color = interactable ? normalColor : disabledColor;
			}
			else
			{
				button.interactable = interactable;
			}
		}
		public void ClearOnClick()
		{
			OnClick = null;
			OnRightClick = null;
		}
		public void OnPointerUp(PointerEventData eventData)
		{
			if (!interactable)
			{
				return;
			}
			if (KInputManager.isFocused)
			{
				KInputManager.SetUserActive();
				//PlaySound(UISoundHelper.ClickOpen);
				if (!eventData.IsPointerMoving())
				{
					button?.OnDeselect(null);
					if (OnRightClick != null)
					{
						if (eventData.button == PointerEventData.InputButton.Right)
						{
							OnRightClick.Invoke();
							return;
						}
					}
					if (OnClick != null)
					{

						if (eventData.button == PointerEventData.InputButton.Left || allowRightClick)
							OnClick?.Invoke();
					}
				}
			}
		}

		public void OnPointerEnter(PointerEventData eventData)
		{
			if (OnPointerEnterAction != null)
				OnPointerEnterAction.Invoke();

			if (!interactable)
			{
				return;
			}

			if (KInputManager.isFocused)
			{
				if (button == null)
				{
					image.color = hoverColor;
				}

				KInputManager.SetUserActive();
				PlaySound(UISoundHelper.MouseOver);
			}
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			if (OnPointerExitAction != null)
				OnPointerExitAction.Invoke();
			if (button == null)
			{
				image.color = normalColor;
			}
			else
			{
				button.OnDeselect(null);
			}
		}

		public void OnPointerDown(PointerEventData eventData)
		{
			if (!interactable)
			{
				return;
			}

			if (KInputManager.isFocused)
			{
				KInputManager.SetUserActive();

				if (!PlayClickSound)
					return;

				if ((OnClick != null && (eventData.button == PointerEventData.InputButton.Left || allowRightClick))
					|| (OnRightClick != null && eventData.button == PointerEventData.InputButton.Right)
					)
					PlaySound(UISoundHelper.ClickOpen);
			}
		}
	}
}
