using UnityEngine;
using HarmonyLib;
using ONI_Together.DebugTools;
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
			"Activate".GetHashCode(),
			"Deactivate".GetHashCode(),
			"SmartReservoirActivate".GetHashCode(),
			"SmartReservoirDeactivate".GetHashCode(),
			"MassageTableActivate".GetHashCode(),
			"MassageTableDeactivate".GetHashCode(),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			var activationRange = go.GetComponent<IActivationRangeTarget>();
			if (activationRange == null) return false;

			int hash = packet.ConfigHash;

			// Handle SmartReservoir specific hashes
			if (hash == "SmartReservoirActivate".GetHashCode())
			{
				activationRange.ActivateValue = packet.Value;
				DebugConsole.Log($"[ActivationRangeHandler] Set SmartReservoir ActivateValue={packet.Value}");
				return true;
			}
			if (hash == "SmartReservoirDeactivate".GetHashCode())
			{
				activationRange.DeactivateValue = packet.Value;
				DebugConsole.Log($"[ActivationRangeHandler] Set SmartReservoir DeactivateValue={packet.Value}");
				return true;
			}

			// Handle MassageTable specific hashes
			if (hash == "MassageTableActivate".GetHashCode())
			{
				activationRange.ActivateValue = packet.Value;
				DebugConsole.Log($"[ActivationRangeHandler] Set MassageTable ActivateValue={packet.Value}");
				return true;
			}
			if (hash == "MassageTableDeactivate".GetHashCode())
			{
				activationRange.DeactivateValue = packet.Value;
				DebugConsole.Log($"[ActivationRangeHandler] Set MassageTable DeactivateValue={packet.Value}");
				return true;
			}

			// Handle generic hashes (e.g., Smart Battery)
			if (hash == "Activate".GetHashCode())
			{
				activationRange.ActivateValue = packet.Value;
				DebugConsole.Log($"[ActivationRangeHandler] Set ActivateValue={packet.Value}");
				return true;
			}
			if (hash == "Deactivate".GetHashCode())
			{
				activationRange.DeactivateValue = packet.Value;
				DebugConsole.Log($"[ActivationRangeHandler] Set DeactivateValue={packet.Value}");
				return true;
			}

			return false;
		}
	}
}
