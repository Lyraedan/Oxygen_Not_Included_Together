using HarmonyLib;
using ONI_Together.DebugTools;

//using PeterHan.PLib.Core;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.lib.UIcmp //Source: Aki
{
	public class FInputField2 : KScreen, IInputHandler
	{
		public static void Postfix(CameraController __instance, ref bool __result)
		{
			if (__result)
				return;
			UnityEngine.EventSystems.EventSystem current = UnityEngine.EventSystems.EventSystem.current;
			if (current == null || current.currentSelectedGameObject == null)
				return;
			if (current.currentSelectedGameObject.GetComponent(nameof(FInputField2)) != null)
				__result = true;
		}
		//static FInputField2()
		//{
		//	string id = nameof(FInputField2);
		//	if (PRegistry.GetData<bool>(id))
		//		return;

		//	try
		//	{
		//		var target = AccessTools.Method(typeof(CameraController), nameof(CameraController.WithinInputField));
		//		var patch = AccessTools.Method(typeof(FInputField2), nameof(FInputField2.Postfix));
		//		new Harmony(id).Patch(target, postfix: new(patch));
		//		PRegistry.PutData(id, true);
		//	}
		//	catch (Exception e)
		//	{
		//		SgtLogger.error("Caught error while patching CameraController.WithinInputField:\n" + e.Message);
		//	}
		//}


		[MyCmpReq]
		public TMP_InputField inputField;

		[SerializeField]
		public string textPath = "Text";

		[SerializeField]
		public string placeHolderPath = "Placeholder";

		private bool allowInputs = true;
		public bool AllowInputs => allowInputs;

		private bool initialized;

		private bool DataTextUpdate = false;
		public void SetTextFromData(string newText)
		{
			DataTextUpdate = true;
			Text = newText;
			//inputField.ForceLabelUpdate();

			DataTextUpdate = false;
		}

		public bool IsEditing()
		{
			return isEditing;
		}

		public string Text
		{
			get => inputField.text;
			set
			{
				if (!initialized)
				{
					// rehook references, these were lost on LocText conversion
#if DEBUG
					DebugConsole.Log("rehooking text input references");
#endif
					inputField.textComponent = inputField.textViewport.transform.Find(textPath).gameObject.AddOrGet<LocText>();
					inputField.placeholder = inputField.textViewport.transform.Find(placeHolderPath).gameObject.AddOrGet<LocText>();

					initialized = true;
				}

				//DebugConsole.Log("setting text " + value);
				//SgtLogger.Assert("inputField", inputField);
				//SgtLogger.Assert("textViewport", inputField.textViewport);
				//SgtLogger.Assert("textcomponent", inputField.textComponent);
				//SgtLogger.Assert("placeholder", inputField.placeholder);

				inputField.text = value;
			}
		}

		public TMP_InputField.OnChangeEvent OnValueChanged => inputField.onValueChanged;
		public TMP_InputField.SubmitEvent OnSubmit => inputField.onSubmit;

		public void AddListener(System.Action<string> onValueChangedEvent)
		{
			inputField.onValueChanged.AddListener((e) =>
			{
				if (!DataTextUpdate)
				{
					onValueChangedEvent(e);
				}
			});
		}


		public override void OnPrefabInit()
		{
			base.OnPrefabInit();
		}

		public override void OnSpawn()
		{
			base.OnSpawn();
			//klei has this check on the camera controler that looks explicitly for KInputTextField and InputField... not normal TMP_InputField
			//adding a disabled one here to get that check to detect the inputfield
			inputField.onFocus += OnEditStart;
			inputField.onEndEdit.AddListener(OnEditEnd);
			Activate();
		}

		public override void OnShow(bool show)
		{
			base.OnShow(show);

			if (show)
			{
				Activate();
				inputField.ActivateInputField();
			}
			else
			{
				Deactivate();
			}
		}

		public void Submit()
		{
			inputField.OnSubmit(null);
		}

		private void OnEditEnd(string input)
		{
			isEditing = false;
			inputField.DeactivateInputField();
		}

		public void ExternalStartEditing() => OnEditStart();

		private void OnEditStart()
		{
			isEditing = true;
			inputField.Select();
			inputField.ActivateInputField();

			KScreenManager.Instance.RefreshStack();
		}
		public void ExternalStopEditing()
		{
			isEditing = false;
			inputField.DeactivateInputField();
			KScreenManager.Instance.RefreshStack();
		}

		public override void OnKeyDown(KButtonEvent e)
		{
			if (!isEditing)
			{
				base.OnKeyDown(e);
				return;
			}

			if (e.TryConsume(Action.Escape))
			{
				inputField.DeactivateInputField();
				e.Consumed = true;
				isEditing = false;
			}

			if (e.TryConsume(Action.DialogSubmit))
			{
				e.Consumed = true;
				inputField.OnSubmit(null);
			}

			if (isEditing)
			{
				e.Consumed = true;
				return;
			}

			if (!e.Consumed)
			{
				base.OnKeyDown(e);
			}
		}

		public void SetInteractable(bool interactable)
		{
			allowInputs = interactable;
			if (inputField != null)
			{
				inputField.interactable = allowInputs;
			}
		}
	}
}