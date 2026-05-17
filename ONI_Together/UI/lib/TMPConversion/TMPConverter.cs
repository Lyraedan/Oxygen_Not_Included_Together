
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.lib
{
	/// <summary>
	/// Credit: Aki
	/// </summary>
	public class TMPConverter
	{
		static TMP_FontAsset NotoSans;
		static TMP_FontAsset GrayStroke;

		public TMPConverter() => Initialize();

		public void Initialize()
		{
			var fonts = new List<TMP_FontAsset>(Resources.FindObjectsOfTypeAll<TMP_FontAsset>());
			NotoSans = fonts.FirstOrDefault(f => f.name == "NotoSans-Regular");
			GrayStroke = fonts.FirstOrDefault(f => f.name == "GRAYSTROKE REGULAR SDF");
		}

		public void ReplaceAllText(GameObject parent, bool realign = true)
		{
			var textComponents = parent.GetComponentsInChildren(typeof(Text), true);

			foreach (Text text in textComponents)
			{
				if (text.gameObject.name == "SettingsDialogData") continue;

				string TMPData = text.text;
				GameObject obj = text.gameObject;
				TMPSettings data = ExtractTMPData(TMPData, text);

				if (data != null)
				{
					var LT = obj.AddComponent<LocText>();
					LT.font = data.Font.Contains("GRAYSTROKE") ? GrayStroke : NotoSans;
					LT.fontStyle = data.FontStyle;
					LT.fontSize = data.FontSize;
					LT.maxVisibleLines = data.MaxVisibleLines;
					LT.textWrappingMode = data.EnableWordWrapping ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
					LT.text = "";
					LT.overflowMode = data.Overflow;
					LT.color = new Color(data.Color[0], data.Color[1], data.Color[2]);
					LT.fontSizeMin = data.VariableFontSizeMinimum;
					LT.fontSizeMax = data.VariableFontSizeMaximum;
					LT.key = data.Content.Replace(" ", string.Empty);
					// alignment isn't carried over instantiation, so it's applied later
					if (realign)
					{
						var importFix = LT.gameObject.AddComponent<TMPImportFix>();
						
						importFix.alignment = data.Alignment;
						importFix.textOverflow = data.Overflow;
						importFix.fontSizeMin = data.VariableFontSizeMinimum;
						importFix.fontSizeMax = data.VariableFontSizeMaximum;
						importFix.autoResize = data.VariableFontSize;
					}
				}
			}
		}

		private static bool isValidJSon(string data)
		{
			if (string.IsNullOrWhiteSpace(data)) return false;
			return data.StartsWith("{") && data.EndsWith("}");
		}

		private static TMPSettings ExtractTMPData(string TMPData, Text text)
		{
			TMPSettings data = null;

			if (isValidJSon(TMPData))
			{
				try
				{
					data = JsonConvert.DeserializeObject<TMPSettings>(TMPData);
				}
				catch (JsonReaderException e)
				{
					Debug.LogWarning("Not valid Json format\n" + e.ToString());
				}

				Object.DestroyImmediate(text);
			}

			return data;
		}
	}
}

