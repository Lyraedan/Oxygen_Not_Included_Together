using ONI_Together.Menus;
using ONI_Together.Networking;
using System.Reflection;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Components
{
	public class UIVisibilityController : MonoBehaviour
	{

		private KToggle pauseButton;
		private ToolTip pauseTooltip;
		private TextStyleSetting tooltipTextStyle;

		private void Start()
		{
			// TODO LATER Plug the visibility tweaks to the lobby entered / left events on SteamLobby

		}

		private void Update()
		{
			using var _ = Profiler.Scope();

			//UpdatePauseButton();
			NetworkIndicatorsScreen.Update();
		}

		void UpdatePauseButton()
		{
			using var _ = Profiler.Scope();

			if (SpeedControlScreen.Instance == null)
				return;

			if (pauseButton == null)
			{
				pauseButton = SpeedControlScreen.Instance?.pauseButtonWidget.GetComponent<KToggle>();
				pauseTooltip = SpeedControlScreen.Instance?.pauseButtonWidget.GetComponent<ToolTip>();

				FieldInfo styleField = typeof(SpeedControlScreen).GetField("TooltipTextStyle", BindingFlags.Instance | BindingFlags.NonPublic);
				tooltipTextStyle = styleField?.GetValue(SpeedControlScreen.Instance) as TextStyleSetting;
			}

			bool allowPause = !MultiplayerSession.InActiveSession;

			pauseButton.interactable = allowPause;

			pauseTooltip.ClearMultiStringTooltip();

			if (MultiplayerSession.InActiveSession)
			{
				// Show custom multiplayer-disabled tooltip
				pauseTooltip.AddMultiStringTooltip("<color=#F44A4A>Can't pause in Multiplayer</color>", tooltipTextStyle);
			}
			else
			{
				// Show default tooltip
				string tip = GameUtil.ReplaceHotkeyString("Pause <color=#F44A4A>[SPACE]</color>", Action.TogglePause);
				pauseTooltip.AddMultiStringTooltip(tip, tooltipTextStyle);
			}
		}
	}
}
