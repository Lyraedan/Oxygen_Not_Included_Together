using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace UI.lib.UIcmp //Source: Aki
{
	public class FInputField : KScreen
	{
		public InputField inputField;
		public InputField.ContentType contentType = InputField.ContentType.Alphanumeric;
		public event System.Action OnStartEdit;
		public event System.Action OnEndEdit;
		public event System.Action<string> OnValueChanged;

		new private bool isEditing;

		public string Value => inputField.text;

		public override void OnPrefabInit()
		{
			base.OnPrefabInit();
			inputField = gameObject.GetComponent<InputField>();
		}

		public override void OnSpawn()
		{
			base.OnSpawn();
			inputField.onEndEdit.AddListener(OnEditEnd);
			inputField.onValueChanged.AddListener(OnChangeValue);
			inputField.contentType = contentType;
		}
		private void OnChangeValue(string input)
		{
			if (!isEditing)
			{
				isEditing = true;
				KScreenManager.Instance.RefreshStack();

				OnStartEdit?.Invoke();
			}
			else
				ProcessInput(input);

			OnValueChanged?.DynamicInvoke(input);
		}
		private void OnEditEnd(string input)
		{
			StartCoroutine(DelayedEndEdit());
		}

		private IEnumerator DelayedEndEdit()
		{
			if (isEditing)
			{
				yield return new WaitForEndOfFrame();
				StopEditing();
			}
			yield break;
		}

		public override void OnKeyDown(KButtonEvent e)
		{
			if (isEditing)
				e.Consumed = true;
		}
		private void StopEditing()
		{
			isEditing = false;
			inputField.DeactivateInputField();
			OnEndEdit?.Invoke();
		}
		protected virtual void ProcessInput(object input)
		{
			string inputString = input.ToString();
			if (!inputString.IsNullOrWhiteSpace())
			{
				SetDisplayValue(inputString);
			}
		}

		public void SetDisplayValue(object input, bool triggerEdit = false)
		{
			if (inputField != null)
			{
				inputField.text = input.ToString();
				if (triggerEdit)
					OnEndEdit?.Invoke();
			}
		}
	}
}
