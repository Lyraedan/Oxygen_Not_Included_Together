using HarmonyLib;
using ONI_MP;
using ONI_MP.DebugTools;
using ONI_MP.Misc;
using UnityEngine;
using UnityEngine.UI;

[HarmonyPatch(typeof(LoadingOverlay), nameof(LoadingOverlay.Load))]
public class LoadingOverlayPatch
{
    public static void Postfix()
    {
        bool enabled = Configuration.GetClientProperty<bool>("PuftAsLoadingIcon");
        if (!enabled)
            return; // Puft loading icon is disabled

        // Replace loading dupe face with custom icon
        var instance = LoadingOverlay.instance;
        var colorfill = instance.transform.Find("ColorFill").GetComponent<Image>();
        if (colorfill != null)
        {
            var replacementColor = new Color(0f, 0.9f, 0.7f, colorfill.color.a);
            colorfill.color = replacementColor;
        }

        var image = instance.transform.Find("Image").GetComponent<Image>();
        var replacementImage = ResourceLoader.LoadEmbeddedSprite("ONI_MP.Assets.mod_mascot.png", out var texture);
        if (replacementImage == null)
            return;

        image.preserveAspect = true;
        image.sprite = replacementImage;
        image.color = Color.white;

        var rect = image.rectTransform;
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 200);
        LogChildren(instance.transform);
    }

    public static void LogChildren(Transform parent, string indent = "")
    {
        foreach (Transform child in parent)
        {
            DebugConsole.Log($"[Loading Screen] {indent}- {child.name}");
            LogChildren(child, indent + "  ");
        }
    }
}
