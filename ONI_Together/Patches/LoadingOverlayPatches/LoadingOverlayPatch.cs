using HarmonyLib;
using ONI_Together;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using UnityEngine;
using UnityEngine.UI;

[HarmonyPatch(typeof(LoadingOverlay), nameof(LoadingOverlay.Load))]
public class LoadingOverlayPatch
{
    public static void Postfix()
    {
        // Replace loading dupe face with custom icon
        var instance = LoadingOverlay.instance;

        bool useCustomColor = Configuration.GetClientProperty<bool>("UseCustomLoadingScreenColor");
        if (useCustomColor)
        {
            var colorfill = instance.transform.Find("ColorFill").GetComponent<Image>();
            if (colorfill != null)
            {
                var replacementColor = new Color(0f, 0.9f, 0.7f, colorfill.color.a);
                colorfill.color = replacementColor;
            }
        }

        bool usePuft = Configuration.GetClientProperty<bool>("PuftAsLoadingIcon");
        if (usePuft)
        {
            var image = instance.transform.Find("Image").GetComponent<Image>();
            var replacementImage = ResourceLoader.LoadEmbeddedSprite("ONI_Together.Assets.mod_mascot.png", out var texture);
            if (replacementImage == null)
                return;

            image.preserveAspect = true;
            image.sprite = replacementImage;
            image.color = Color.white;

            var rect = image.rectTransform;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 200);
        }
    }
}
