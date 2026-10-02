using Newtonsoft.Json;
using ONI_Together.DebugTools;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UI.lib.UIcmp //Source: Aki
{
	public class FScreen : KScreen
	{
		public const float SCREEN_SORT_KEY = 300f;

#pragma warning disable IDE0051 // Remove unused private members
		new bool ConsumeMouseScroll = true; // do not remove!!!!
#pragma warning restore IDE0051 // Remove unused private members
		private bool shown = false;
		public bool pause = true;
		public bool lockCam = true;

		public override void OnPrefabInit()
		{
			activateOnSpawn = true;
			gameObject.SetActive(true);
		}


		public virtual void ShowDialog()
		{
			if (transform.parent.GetComponent<Canvas>() == null && transform.parent.parent != null)
			{
				transform.SetParent(transform.parent.parent);
			}
			transform.SetAsLastSibling();
		}

		public virtual void OnClickCancel()
		{
			DebugConsole.Log("cancel");
			Reset();
			Deactivate();
		}

		public virtual void Reset()
		{
		}

		public virtual void OnClickApply()
		{
		}

		#region generic kscreen behaviour
		public override void OnCmpEnable()
		{
			base.OnCmpEnable();
			if (lockCam && CameraController.Instance != null)
			{
				CameraController.Instance.DisableUserCameraControl = true;
			}
		}

		public override void OnCmpDisable()
		{
			base.OnCmpDisable();
			if (lockCam && CameraController.Instance != null)
			{
				CameraController.Instance.DisableUserCameraControl = false;
			}
			Trigger((int)GameHashes.Close, null);
		}

		public override bool IsModal()
		{
			return true;
		}

		public override float GetSortKey()
		{
			return SCREEN_SORT_KEY;
		}

		public override void OnActivate()
		{
			OnShow(true);
		}

		public override void OnDeactivate()
		{
			OnShow(false);
		}

		public override void OnShow(bool show)
		{
			base.OnShow(show);

			// In a multiplayer session these dialogs must not touch the pause stack - the
			// same policy ModalPauseScreen_PreventPauses applies to ONI's own screens.
			// SpeedControlScreen's pause is a COUNTER and the gate blocks the Unpause half
			// of this pair, so each open/close during a join leaks one pause level.
			//
			// pausedSim, not a bare session check on both sides: the unpause must mirror
			// what the open actually did, or a dialog opened outside a session and closed
			// inside one (or the other way round) leaks a level again.
			if (pause && SpeedControlScreen.Instance != null)
			{
				if (show && !shown)
				{
					if (!ONI_Together.Networking.MultiplayerSession.InActiveSession)
					{
						SpeedControlScreen.Instance.Pause(false);
						pausedSim = true;
					}
				}
				else if (!show && shown)
				{
					if (pausedSim)
					{
						SpeedControlScreen.Instance.Unpause(false);
						pausedSim = false;
					}
				}
				shown = show;
			}
		}

		private bool pausedSim = false;


		public override void OnKeyUp(KButtonEvent e)
		{
			if (!e.Consumed)
			{
				KScrollRect scroll_rect = GetComponentInChildren<KScrollRect>();
				if (scroll_rect != null)
				{
					scroll_rect.OnKeyUp(e);
				}
			}
			e.Consumed = true;
		}
		#endregion

	}
}
