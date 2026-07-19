using UnityEngine;
using HarmonyLib;
using ONI_Together.DebugTools;
using Shared;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>
	/// Handles IActivationRangeTarget buildings (SmartReservoir, BatterySmart, MassageTable).
	/// </summary>
	public class ActivationRangeHandler : IBuildingConfigHandler
	{
		private static readonly int[] _hashes = new int[]
		{
			NetworkingHash.ForConfigKey("Activate"),
			NetworkingHash.ForConfigKey("Deactivate"),
			NetworkingHash.ForConfigKey("SmartReservoirActivate"),
			NetworkingHash.ForConfigKey("SmartReservoirDeactivate"),
			NetworkingHash.ForConfigKey("MassageTableActivate"),
			NetworkingHash.ForConfigKey("MassageTableDeactivate"),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			var activationRange = go.GetComponent<IActivationRangeTarget>();
			if (activationRange == null || packet.ConfigType != BuildingConfigType.Float
			    || !BuildingConfigPacket.IsInRange(packet.Value, 0f, 100f)) return false;

			int hash = packet.ConfigHash;

			// Handle SmartReservoir specific hashes
			if (hash == NetworkingHash.ForConfigKey("SmartReservoirActivate"))
			{
				activationRange.ActivateValue = packet.Value;
				//DebugConsole.Log($"[ActivationRangeHandler] Set SmartReservoir ActivateValue={packet.Value}");
				return true;
			}
			if (hash == NetworkingHash.ForConfigKey("SmartReservoirDeactivate"))
			{
				activationRange.DeactivateValue = packet.Value;
				//DebugConsole.Log($"[ActivationRangeHandler] Set SmartReservoir DeactivateValue={packet.Value}");
				return true;
			}

			// Handle MassageTable specific hashes
			if (hash == NetworkingHash.ForConfigKey("MassageTableActivate"))
			{
				activationRange.ActivateValue = packet.Value;
				//DebugConsole.Log($"[ActivationRangeHandler] Set MassageTable ActivateValue={packet.Value}");
				return true;
			}
			if (hash == NetworkingHash.ForConfigKey("MassageTableDeactivate"))
			{
				activationRange.DeactivateValue = packet.Value;
				//DebugConsole.Log($"[ActivationRangeHandler] Set MassageTable DeactivateValue={packet.Value}");
				return true;
			}

			// Handle generic hashes (e.g., Smart Battery)
			if (hash == NetworkingHash.ForConfigKey("Activate"))
			{
				activationRange.ActivateValue = packet.Value;
				//DebugConsole.Log($"[ActivationRangeHandler] Set ActivateValue={packet.Value}");
				return true;
			}
			if (hash == NetworkingHash.ForConfigKey("Deactivate"))
			{
				activationRange.DeactivateValue = packet.Value;
				//DebugConsole.Log($"[ActivationRangeHandler] Set DeactivateValue={packet.Value}");
				return true;
			}

			return false;
		}
	}
}
