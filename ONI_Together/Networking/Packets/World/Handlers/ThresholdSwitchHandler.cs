using UnityEngine;
using ONI_Together.DebugTools;
using Shared;
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
			NetworkingHash.ForConfigKey("Threshold"),
			NetworkingHash.ForConfigKey("ThresholdDirection"),
			NetworkingHash.ForConfigKey("ThresholdDir"),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			var thresholdSwitch = go.GetComponent<IThresholdSwitch>();
			if (thresholdSwitch == null) return false;

			int hash = packet.ConfigHash;

			if (hash == NetworkingHash.ForConfigKey("Threshold"))
			{
				if (packet.ConfigType != BuildingConfigType.Float
				    || !BuildingConfigPacket.IsInRange(packet.Value, -1_000_000_000f, 1_000_000_000f))
					return false;
				thresholdSwitch.Threshold = packet.Value;
				//DebugConsole.Log($"[ThresholdSwitchHandler] Set Threshold={packet.Value} on {go.name}");
				return true;
			}

			if (hash == NetworkingHash.ForConfigKey("ThresholdDirection") || hash == NetworkingHash.ForConfigKey("ThresholdDir"))
			{
				if (packet.ConfigType != BuildingConfigType.Boolean
				    || !BuildingConfigPacket.IsBooleanValue(packet.Value))
					return false;
				thresholdSwitch.ActivateAboveThreshold = packet.Value > 0.5f;
				//DebugConsole.Log($"[ThresholdSwitchHandler] Set ActivateAboveThreshold={packet.Value > 0.5f} on {go.name}");
				return true;
			}

			return false;
		}
	}
}
