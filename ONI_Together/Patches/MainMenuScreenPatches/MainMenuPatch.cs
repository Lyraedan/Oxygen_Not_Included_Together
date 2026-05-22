using HarmonyLib;
using ONI_Together;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.UI;
using Steamworks;
using System;
using System.Linq;
using System.Reflection;
using Shared.Profiling;
using UnityEngine;
using UnityEngine.UI;

[HarmonyPatch(typeof(MainMenu), "OnPrefabInit")]
internal static class MainMenuPatch
{
	private static GameObject staticBgGO;

	public static void Postfix(MainMenu __instance)
	{
		using var _ = Profiler.Scope();

		int normalFontSize = 20;
		var normalStyle = __instance.normalButtonStyle;

		var buttonInfoType = __instance.GetType().GetNestedType("ButtonInfo", BindingFlags.NonPublic);

		var makeButton = __instance.GetType().GetMethod("MakeButton", BindingFlags.NonPublic | BindingFlags.Instance);

		// Multiplayer - Opens the multiplayer screen with all options
		var multiplayerInfo = CreateButtonInfo(
				ONI_Together.STRINGS.UI.MAINMENU.MULTIPLAYER.LABEL,
				new System.Action(() =>
				{
					UnityMultiplayerScreen.OpenFromMainMenu();
					return;
					// Open the multiplayer screen
					var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
					if (canvas != null)
					{
						ONI_Together.Menus.MultiplayerScreen.Show(canvas.transform);
					}
				}),
				normalFontSize,
				normalStyle,
				buttonInfoType
		);
		makeButton.Invoke(__instance, new object[] { multiplayerInfo });

		//UpdatePromos();
		//UpdateDLC();
		//UpdateBuildNumber();
		AddSocials(__instance);

		UpdateLogo();
		UpdatePlacements(__instance);
	}

	// Reflection utility to build ButtonInfo struct
	private static object CreateButtonInfo(string text, System.Action action, int fontSize, ColorStyleSetting style, Type buttonInfoType)
	{
		using var _ = Profiler.Scope();

		var buttonInfo = Activator.CreateInstance(buttonInfoType);
		buttonInfoType.GetField("text").SetValue(buttonInfo, new LocString(text));
		buttonInfoType.GetField("action").SetValue(buttonInfo, action);
		buttonInfoType.GetField("fontSize").SetValue(buttonInfo, fontSize);
		buttonInfoType.GetField("style").SetValue(buttonInfo, style);
		return buttonInfo;
	}

	private static void UpdatePlacements(MainMenu __instance)
	{
		using var _ = Profiler.Scope();

		var buttonParent = __instance.buttonParent;
		if (buttonParent != null)
		{
			var children = buttonParent.GetComponentsInChildren<KButton>(true);

			// Find Multiplayer button
			var multiplayerBtn = children.FirstOrDefault(b => b.GetComponentInChildren<LocText>()?.text.ToUpper().Contains(ONI_Together.STRINGS.UI.MAINMENU.MULTIPLAYER.LABEL) == true);

			int siblingIndex = children.Length >= 10 ? 4 : 3;
			multiplayerBtn.transform.SetSiblingIndex(siblingIndex);
		}
	}

	private static void UpdateLogo()
	{
		using var _ = Profiler.Scope();

		// Attempt to find and replace the logo
		GameObject logoObj = GameObject.Find("Logo");
		if (logoObj != null)
		{
			var image = logoObj.GetComponent<UnityEngine.UI.Image>();
			if (image != null)
			{
				Texture2D tex = ResourceLoader.LoadEmbeddedTexture("ONI_Together.Assets.oni_together_logo.png");
				if (tex != null)
				{
					Sprite newSprite = Sprite.Create(
							tex,
							new Rect(0, 0, tex.width, tex.height),
							new Vector2(0.5f, 0.5f)
					);
					image.sprite = newSprite;
				}
			}
		}

	}

    // Rework this to add a vertical layout group on the bottom left side of the screen
    private static void AddSocials(MainMenu menu)
    {
        using var _ = Profiler.Scope();

        GameObject socialsContainer = new GameObject("ONI_Together_SocialsContainer", typeof(RectTransform));
        socialsContainer.transform.SetParent(menu.transform, false);

        RectTransform socialsRect = socialsContainer.GetComponent<RectTransform>();
        socialsRect.anchorMin = new Vector2(0f, 0f);
        socialsRect.anchorMax = new Vector2(0f, 0f);
        socialsRect.pivot = new Vector2(0f, 0f);
        socialsRect.anchoredPosition = new Vector2(20f, 20f);

        var layout = socialsContainer.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.LowerLeft;
        layout.spacing = 10f;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        layout.childControlHeight = false;
        layout.childControlWidth = false;

        var discordSprite = ResourceLoader.LoadEmbeddedTexture("ONI_Together.Assets.discord.png");
        AddSocialButton(socialsContainer.transform, ONI_Together.STRINGS.UI.MAINMENU.DISCORD_INFO, "https://discord.gg/jpxveK6mmY", discordSprite);

        int buttonCount = socialsContainer.transform.childCount;
        float buttonHeight = 96f;
        float totalHeight = buttonCount * buttonHeight + (buttonCount - 1) * layout.spacing;
        socialsRect.sizeDelta = new Vector2(100f, totalHeight);
    }

    private static void AddSocialButton(Transform parent, string tooltip, string url, Texture2D spriteSheet)
	{
		using var _ = Profiler.Scope();

		if (spriteSheet == null)
			return;

		GameObject buttonGO = new GameObject($"SocialButton_{tooltip}", typeof(RectTransform));
		buttonGO.transform.SetParent(parent, false);

		var buttonImage = buttonGO.AddComponent<Image>();

		var button = buttonGO.AddComponent<Button>();

		var rectTransform = button.GetComponent<RectTransform>();
		rectTransform.sizeDelta = new Vector2(96f, 96f);

		// slice the spritesheet (3 frames horizontally)
		Sprite normalSprite = Sprite.Create(spriteSheet, new Rect(0, 0, 512, 512), new Vector2(0.5f, 0.5f));
		Sprite highlightedSprite = Sprite.Create(spriteSheet, new Rect(512, 0, 512, 512), new Vector2(0.5f, 0.5f));
		Sprite pressedSprite = Sprite.Create(spriteSheet, new Rect(1024, 0, 512, 512), new Vector2(0.5f, 0.5f));

		buttonImage.sprite = normalSprite;

		var spriteState = new SpriteState
		{
			highlightedSprite = highlightedSprite,
			pressedSprite = pressedSprite
		};
		button.spriteState = spriteState;
		button.transition = Selectable.Transition.SpriteSwap;

		var tooltipComp = buttonGO.AddComponent<ToolTip>();
		tooltipComp.toolTip = tooltip;

		button.onClick.AddListener(() =>
		{
			Application.OpenURL(url);
		});
	}
}
