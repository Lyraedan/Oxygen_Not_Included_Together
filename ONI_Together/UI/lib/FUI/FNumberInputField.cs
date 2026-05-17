using System;
using UnityEngine;

namespace UI.lib.UIcmp //Source: Aki
{
	public class FNumberInputField : FInputField
	{
		public int maxValue = int.MaxValue;
		public int minValue = int.MinValue;

		public float GetFloat
		{
			get
			{
				float.TryParse(inputField.text, out float val);
				val = Mathf.Clamp(val, minValue, maxValue);
				return val;
			}
		}

		// approximate
		public T GetValue<T>()
		{
			if (float.TryParse(inputField.text, out float val))
			{
				val = Mathf.Clamp(val, minValue, maxValue);
				T result = (T)Convert.ChangeType(val, typeof(T));
				return result;
			}

			return default;
		}
	}
}
