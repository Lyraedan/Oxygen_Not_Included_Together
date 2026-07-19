using UnityEngine;
using ONI_Together.DebugTools;
using Shared;
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
			NetworkingHash.ForConfigKey("Slider"),
			NetworkingHash.ForConfigKey("SliderIndex"),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			int hash = packet.ConfigHash;

			// Handle single slider control
			if (hash == NetworkingHash.ForConfigKey("Slider"))
			{
				var singleSlider = go.GetComponent<ISingleSliderControl>();
				if (singleSlider != null)
				{
					try
					{
						if (packet.ConfigType != BuildingConfigType.Float || packet.SliderIndex != 0
						    || !BuildingConfigPacket.IsInRange(
							    packet.Value, singleSlider.GetSliderMin(0), singleSlider.GetSliderMax(0)))
							return false;
						singleSlider.SetSliderValue(packet.Value, 0);
						packet.Value = singleSlider.GetSliderValue(0);
					}
					catch (System.Exception e)
					{
						DebugConsole.Log($"[SliderControlHandler] Warning: SetSliderValue triggered exception on {go.name}: {e.Message}");
						return false;
					}
					return true;
				}
			}

			// Handle indexed slider control
			if (hash == NetworkingHash.ForConfigKey("SliderIndex") && packet.ConfigType == BuildingConfigType.SliderIndex)
			{
				var sliderControl = go.GetComponent<ISliderControl>();
				if (sliderControl != null)
				{
					try
					{
						float minimum = sliderControl.GetSliderMin(packet.SliderIndex);
						float maximum = sliderControl.GetSliderMax(packet.SliderIndex);
						if (!BuildingConfigPacket.IsInRange(packet.Value, minimum, maximum))
							return false;
						sliderControl.SetSliderValue(packet.Value, packet.SliderIndex);
						packet.Value = sliderControl.GetSliderValue(packet.SliderIndex);
					}
					catch (System.Exception e)
					{
						DebugConsole.Log($"[SliderControlHandler] Warning: SetSliderValue triggered exception on {go.name}: {e.Message}");
						return false;
					}
					return true;
				}
			}

			return false;
		}
	}
}
