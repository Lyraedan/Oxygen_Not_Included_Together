using HarmonyLib;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.UI;
using ONI_Together.UI.lib;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ONI_Together.Patches.InterfacePatches
{
	internal class DetailsScreen_Patches
	{

		static GameObject LinkInChatButton = null;
		[HarmonyPatch(typeof(DetailsScreen), nameof(DetailsScreen.OnPrefabInit))]
        public class DetailsScreen_OnPrefabInit_Patch
        {
            public static void Postfix(DetailsScreen __instance)
			{
				if (LinkInChatButton != null)
					UnityEngine.Object.Destroy(LinkInChatButton);

				var chatLinkBtn = Util.KInstantiateUI<KButton>(__instance.CodexEntryButton.gameObject, __instance.CodexEntryButton.transform.parent.gameObject);
				//SkinButton.rectTransform().SetInsetAndSizeFromParentEdge(RectTransform.Edge.Right, 20, 33f);
				chatLinkBtn.ClearOnClick();
				chatLinkBtn.name = "LinkInChatButton";
				UIUtils.AddSimpleTooltipToObject(chatLinkBtn.transform, STRINGS.UI.MP_CHATBOX.LINK_IN_CHAT.TOOLTIP, true, onBottom: true);
				if (chatLinkBtn.transform.Find("Image").TryGetComponent<Image>(out var image))
				{
					image.sprite = ResourceLoader.LoadEmbeddedSprite("ONI_Together.Assets.oni_together_link_chat.png", out _);
				}
				chatLinkBtn.onClick += () =>
				{
					LinkItemInChat(__instance);
				};
				LinkInChatButton = chatLinkBtn.gameObject;
			}
        }


		static void LinkItemInChat(DetailsScreen instance)
		{
			if (instance.target == null)
				return;

			var name = global::STRINGS.UI.StripLinkFormatting(instance.target.GetProperName());
			string id = instance.CodexEntryButton_GetCodexId();
			var linkText = global::STRINGS.UI.FormatAsLink(name, id);
			UnityChatBoxUI.AddLinkToChatInput(linkText);
		}

		[HarmonyPatch(typeof(DetailsScreen), nameof(DetailsScreen.CodexEntryButton_Refresh))]
		public class DetailsScreen_CodexEntryButton_Refresh_Patch
		{
			public static void Postfix(DetailsScreen __instance)
			{
				string text = __instance.CodexEntryButton_GetCodexId();
				LinkInChatButton.SetActive(MultiplayerSession.InSession);
				LinkInChatButton.GetComponent<KButton>().isInteractable = text != "";
			}
		}
	}
}
