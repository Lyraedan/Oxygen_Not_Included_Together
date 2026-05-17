using HarmonyLib;
using JetBrains.Annotations;
using ONI_Together.Networking;
using ONI_Together.Networking.Transport.Steamworks;
using ONI_Together.UI;
using Steamworks;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Shared.Profiling;
using UnityEngine;
using UnityEngine.Events;

namespace ONI_Together.Patches
{
	[HarmonyPatch]
	public static class PauseScreenPatch
	{
		// This method is called when "Quit" is confirmed in the pause menu
		[HarmonyPatch(typeof(PauseScreen), "OnQuitConfirm")]
		[HarmonyPrefix]
		[UsedImplicitly]
		public static void OnQuitConfirm_Prefix(bool saveFirst)
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.InSession)
			{
				NetworkConfig.Stop();
				MultiplayerSession.Clear();
			}
		}

		// This prevents the game from pausing when the PauseScreen opens in multiplayer
		[HarmonyPatch(typeof(SpeedControlScreen), nameof(SpeedControlScreen.Pause))]
		[HarmonyPrefix]
		[UsedImplicitly]
		public static bool PreventPauseInMultiplayer(bool playSound = true, bool isCrashed = false)
		{
			// Restore pause functionality
			/*if (MultiplayerSession.InSession && !isCrashed)
			{
					return false;
			}*/

			return true;
		}

		[HarmonyPatch(typeof(PauseScreen), "ConfigureButtonInfos")]
		public static class PauseScreen_AddInviteButton
		{
			public static void Postfix(PauseScreen __instance)
			{
				using var _ = Profiler.Scope();

				var buttonInfos = __instance.buttons;

                // Only in multiplayer
                if (!MultiplayerSession.InSession)
				{
					AddButton(__instance, STRINGS.UI.PAUSESCREEN.HOSTGAME.LABEL, () =>
					{
						PauseScreen.Instance.Show(false); // Hide pause screen
						UnityMultiplayerScreen.OpenFromPauseScreen();
						return;
						// Show lobby config screen - it will handle lobby creation
						var canvas = Object.FindFirstObjectByType<Canvas>();
						if (canvas != null)
						{
							UnityMultiplayerScreen.OpenFromPauseScreen();
							ONI_Together.Menus.HostLobbyConfigScreen.Show(canvas.transform, () =>
							{
								NetworkConfig.StartServer();
							});
						}
                    });
                    return;
				}

				// In multiplayer session - show single Multiplayer button
				AddButton(__instance, STRINGS.UI.PAUSESCREEN.MULTIPLAYER.LABEL, () =>
				{
					PauseScreen.Instance.Show(false); // Hide pause screen
					UnityLobbyStateDialogueUI.ShowLobbyStateWindow();
					return;
					// Show multiplayer info screen
					var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
					if (canvas != null)
					{
						ONI_Together.Menus.MultiplayerInfoScreen.Show(canvas.transform);
					}
				});
            }
		}

		[HarmonyPatch(typeof(KModalScreen), nameof(KModalScreen.OnShow), new[] { typeof(bool) })]
		public static class ModalPauseScreen_PreventPauses
		{
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
			{
				using var _ = Profiler.Scope();

                var pauseTarget = AccessTools.Method(
					typeof(SpeedControlScreen),
					nameof(SpeedControlScreen.Pause),
					new[] { typeof(bool), typeof(bool) });
                var unpauseTarget = AccessTools.Method(
					typeof(SpeedControlScreen),
					nameof(SpeedControlScreen.Unpause),
					new[] { typeof(bool) });
				var pauseReplacement = AccessTools.Method(
					typeof(ModalPauseScreen_PreventPauses),
					nameof(ModalPauseScreen_PreventPauses.ConditionalPause),
					new[] { typeof(SpeedControlScreen), typeof(bool), typeof(bool) });
				var unpauseReplacement = AccessTools.Method(
					typeof(ModalPauseScreen_PreventPauses),
					nameof(ModalPauseScreen_PreventPauses.ConditionalUnpause),
					new[] { typeof(SpeedControlScreen), typeof(bool) });

                foreach (var inst in insts)
				{
					// Replace the method call to pause or unpause with our method.
					if (inst.Calls(pauseTarget))
					{
						yield return new CodeInstruction(OpCodes.Call, pauseReplacement);
					}
					else if (inst.Calls(unpauseTarget))
					{
						yield return new CodeInstruction(OpCodes.Call, unpauseReplacement);
					}
					else
					{
						yield return inst;
					}
				}
			}

			static void ConditionalPause(SpeedControlScreen inst, bool playSound, bool isCrash)
			{
				using var _ = Profiler.Scope();

				// Only pause if we arent in a multiplayer session
				if (MultiplayerSession.InSession) return;

				SpeedControlScreen.Instance.Pause(playSound, isCrash);
			}

            static void ConditionalUnpause(SpeedControlScreen inst, bool playSound)
            {
	            using var _ = Profiler.Scope();

				// Only unpause if we arent in a multiplayer session
				if (MultiplayerSession.InSession) return;

				SpeedControlScreen.Instance.Unpause(playSound);
            }
        }

		private static void AddButton(PauseScreen __instance, string label, System.Action onClicked, string placeAfter = "Resume")
		{
			using var _ = Profiler.Scope();

			var buttonInfos = __instance.buttons.ToList();
            if (buttonInfos.Any(b => b.text == label))
                return; // Ignore duplicates

            int id_x = buttonInfos.FindIndex(b => b.text == placeAfter) + 1;
            if (id_x <= 0) id_x = 1;

            buttonInfos.Insert(id_x, new KModalButtonMenu.ButtonInfo(
                    label,
                    new UnityAction(() =>
                    {
						onClicked.Invoke();
                    })
            ));

			__instance.buttons = buttonInfos.ToArray();
        }
	}
}
