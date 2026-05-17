using UnityEngine;
using UI.lib.UIcmp;

namespace UI.lib.UI.FUI.Unity_UI_Extensions.Scripts.Controls.Sliders
{
	internal class MinMaxSliderAudio : KMonoBehaviour
	{
		private readonly float movePlayRate = 0.01f;
		private float lastMoveTime;
		private float lastMoveValue;
		private bool playedBoundaryBump;

		MinMaxSlider slider;

		public override void OnSpawn()
		{
			base.OnSpawn();
			slider = GetComponent<MinMaxSlider>();
		}

		public void OnDragEnd()
		{
			if (KInputManager.isFocused)
			{
				PlaySound(UISoundHelper.SliderEnd);
			}
		}
		public void OnDragStart()
		{
			if (KInputManager.isFocused)
			{
				PlaySound(UISoundHelper.SliderStart);
			}
		}
		public void OnDrag(bool isMaxSlider)
		{
			if (KInputManager.isFocused)
			{
				PlayMoveSound(isMaxSlider);
			}
		}

		public void PlayMoveSound(bool MaxSlider)
		{
			///*
			if (KInputManager.isFocused)
			{
				float timeSinceLast = Time.unscaledTime - lastMoveTime;
				if (!(timeSinceLast < movePlayRate))
				{
					float inverseLerpValue = Mathf.InverseLerp(slider.MinLimit, slider.MaxLimit, MaxSlider ? slider.MaxValue : slider.MinValue);
					string sound_path = null;
					if (inverseLerpValue == 1f && lastMoveValue == 1f)
					{
						if (!playedBoundaryBump)
						{
							sound_path = UISoundHelper.SliderBoundaryHigh;
							playedBoundaryBump = true;
						}
					}
					else
					{
						if (inverseLerpValue == 0f && lastMoveValue == 0f)
						{
							if (!playedBoundaryBump)
							{
								sound_path = UISoundHelper.SliderBoundaryLow;
								playedBoundaryBump = true;
							}
						}
						else if (inverseLerpValue >= 0f && inverseLerpValue <= 1f)
						{
							sound_path = UISoundHelper.SliderMove;
							playedBoundaryBump = false;
						}
					}
					if (sound_path != null && sound_path.Length > 0)
					{
						lastMoveTime = Time.unscaledTime;
						lastMoveValue = inverseLerpValue;
						FMOD.Studio.EventInstance ev = KFMOD.BeginOneShot(sound_path, Vector3.zero, 1f);
						ev.setParameterByName("sliderValue", inverseLerpValue);
						ev.setParameterByName("timeSinceLast", timeSinceLast);
						KFMOD.EndOneShot(ev);
					}
				}
			}//*/
		}
	}
}
