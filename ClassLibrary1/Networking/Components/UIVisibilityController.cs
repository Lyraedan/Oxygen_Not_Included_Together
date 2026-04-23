using ONI_MP.Menus;
using ONI_MP.Networking;
using System.Reflection;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Components
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

	}
}
