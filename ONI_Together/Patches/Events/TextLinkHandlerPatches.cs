using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Shared.Profiling;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ONI_Together.Patches.Events
{
    public class TextLinkHandlerPatches
    {
        [HarmonyPatch(typeof(TextLinkHandler), "OnPointerClick")]
        public static class TextLinkHandler_OnPointerClick_Patch
        {
            public static void Postfix(TextLinkHandler __instance, UnityEngine.EventSystems.PointerEventData eventData)
            {
                return;
                // Disabled, interfers with the DB
                using var _ = Profiler.Scope();

                if (eventData.button != PointerEventData.InputButton.Left || !__instance.text.AllowLinks)
                    return;
                int intersectingLink = TMP_TextUtilities.FindIntersectingLink((TMP_Text)__instance.text, KInputManager.GetMousePos(), (Camera)null);
                if (intersectingLink == -1)
                    return;

                TMP_LinkInfo linkInfo = ((TMP_Text)__instance.text).textInfo.linkInfo[intersectingLink];
                string linkID = linkInfo.GetLinkID();

                if (MultiplayerMod.UseSteamOverlay && SteamUtils.IsOverlayEnabled())
                    SteamFriends.ActivateGameOverlayToWebPage(linkID);
                else
                    App.OpenWebURL(linkID);
            }
        }
    }
}
