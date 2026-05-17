using UnityEngine;
using ONI_Together.DebugTools;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>
	/// Handles IThresholdSwitch buildings (temperature, pressure, gas, etc. sensors).
	/// </summary>
	public class ThresholdSwitchHandler : IBuildingConfigHandler
	{
		private static readonly int[] _hashes = new int[]
		{
			"Threshold".GetHashCode(),
			"ThresholdDirection".GetHashCode(),
			"ThresholdDir".GetHashCode(),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			var thresholdSwitch = go.GetComponent<IThresholdSwitch>();
			if (thresholdSwitch == null) return false;

			int hash = packet.ConfigHash;

			if (hash == "Threshold".GetHashCode())
			{
				thresholdSwitch.Threshold = packet.Value;
				DebugConsole.Log($"[ThresholdSwitchHandler] Set Threshold={packet.Value} on {go.name}");
				return true;
			}

			if (hash == "ThresholdDirection".GetHashCode() || hash == "ThresholdDir".GetHashCode())
			{
				thresholdSwitch.ActivateAboveThreshold = packet.Value > 0.5f;
				DebugConsole.Log($"[ThresholdSwitchHandler] Set ActivateAboveThreshold={packet.Value > 0.5f} on {go.name}");
				return true;
			}

			return false;
		}
	}
}
