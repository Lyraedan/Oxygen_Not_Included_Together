using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.UI;
using UI.lib.UIcmp;

namespace UI.lib.UI.FUI
{
	public class PasswordInputVisibilityToggle : KMonoBehaviour
	{
		FButton PasswordToggle;
		Image SlashImage;
		FInputField2 PasswordInput;
		public bool PasswordVisible = false;

		public void InitEyeToggle(FInputField2 input, string slashImagePath = "Slash")
		{
			PasswordInput = input;
			SlashImage = transform.Find(slashImagePath).GetComponent<Image>();
			PasswordToggle = gameObject.AddOrGet<FButton>();
			PasswordToggle.OnClick += TogglePasswordVisibility;
			SetPasswordVisibility(false);
		}
		public void TogglePasswordVisibility() => SetPasswordVisibility(!PasswordVisible);

		public void SetPasswordVisibility(bool passwordVisible)
		{
			PasswordVisible = passwordVisible;
			SlashImage.gameObject.SetActive(!passwordVisible);
			PasswordInput.inputField.contentType = passwordVisible ? TMPro.TMP_InputField.ContentType.Standard : TMPro.TMP_InputField.ContentType.Password;
			PasswordInput.inputField.ForceLabelUpdate();
		}
	}
}
