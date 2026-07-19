using UnityEngine;
using ONI_Together.DebugTools;
using Shared;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>
	/// Handles IUserControlledCapacity buildings (reservoirs, storages).
	/// </summary>
	public class CapacityHandler : IBuildingConfigHandler
	{
		private static readonly int[] _hashes = new int[]
		{
			NetworkingHash.ForConfigKey("Capacity"),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			if (packet.ConfigHash != NetworkingHash.ForConfigKey("Capacity")) return false;

			var capacityControl = go.GetComponent<IUserControlledCapacity>();
			if (capacityControl == null) return false;
			if (packet.ConfigType != BuildingConfigType.Float
			    || !BuildingConfigPacket.IsInRange(
				    packet.Value, capacityControl.MinCapacity, capacityControl.MaxCapacity)
			    || capacityControl.WholeValues && !BuildingConfigPacket.IsIntegralValue(packet.Value))
				return false;

			capacityControl.UserMaxCapacity = packet.Value;
			//DebugConsole.Log($"[CapacityHandler] Set UserMaxCapacity={packet.Value} on {go.name}");
			return true;
		}
	}
}
