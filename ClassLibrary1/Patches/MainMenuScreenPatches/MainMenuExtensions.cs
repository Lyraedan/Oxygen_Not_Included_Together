using ONI_MP.DebugTools;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Patches.MainMenuScreenPatches
{
	public static class MainMenuExtensions
	{
		public static void AddClonedButton(this MainMenu menu, string text, string small_text, bool highlight, System.Action action)
		{
			using var _ = Profiler.Scope();

			var sourceButton = menu.Button_NewGame;
			if (sourceButton == null)
			{
				DebugConsole.LogError("Button_NewGame is null");
				return;
			}

			GameObject newButtonGO = UnityEngine.Object.Instantiate(sourceButton.gameObject, sourceButton.transform.parent);
			newButtonGO.name = $"MPButton_{text.Replace(" ", "")}";

			sourceButton.ClearOnClick();
            var newButton = newButtonGO.GetComponent<KButton>();
			var texts = newButtonGO.GetComponentsInChildren<LocText>(includeInactive: true);

			// Set primary text (title)
			if (texts.Length > 0)
				texts[0].text = text;

			// Set secondary text (subtitle) to empty string
			if (texts.Length > 1)
				texts[1].text = small_text;

			newButton.onClick += action;

			DebugConsole.Log($"Button '{text}' added to main menu.");
		}

	}
}
