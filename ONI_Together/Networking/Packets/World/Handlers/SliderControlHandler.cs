using UnityEngine;
using ONI_Together.DebugTools;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>
	/// Handles ISliderControl and ISingleSliderControl buildings.
	/// </summary>
	public class SliderControlHandler : IBuildingConfigHandler
	{
		private static readonly int[] _hashes = new int[]
		{
			"Slider".GetHashCode(),
			"SliderIndex".GetHashCode(),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			int hash = packet.ConfigHash;

			// Handle single slider control
			if (hash == "Slider".GetHashCode())
			{
				var singleSlider = go.GetComponent<ISingleSliderControl>();
				if (singleSlider != null)
				{
					try
					{
						singleSlider.SetSliderValue(packet.Value, -1);
						DebugConsole.Log($"[SliderControlHandler] Set SingleSlider value={packet.Value} on {go.name}");
					}
					catch (System.Exception e)
					{
						DebugConsole.Log($"[SliderControlHandler] Warning: SetSliderValue triggered exception on {go.name}: {e.Message}");
					}
					return true;
				}
			}

			// Handle indexed slider control
			if (hash == "SliderIndex".GetHashCode() && packet.ConfigType == BuildingConfigType.SliderIndex)
			{
				var sliderControl = go.GetComponent<ISliderControl>();
				if (sliderControl != null)
				{
					try
					{
						int sliderIndex = (int)(packet.Value / 1000000f);
						float actualValue = packet.Value - (sliderIndex * 1000000f);
						sliderControl.SetSliderValue(actualValue, sliderIndex);
						DebugConsole.Log($"[SliderControlHandler] Set Slider[{sliderIndex}]={actualValue} on {go.name}");
					}
					catch (System.Exception e)
					{
						DebugConsole.Log($"[SliderControlHandler] Warning: SetSliderValue triggered exception on {go.name}: {e.Message}");
					}
					return true;
				}
			}

			return false;
		}
	}
}
