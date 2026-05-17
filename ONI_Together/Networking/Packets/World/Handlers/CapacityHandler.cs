using UnityEngine;
using ONI_Together.DebugTools;
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
			"Capacity".GetHashCode(),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			if (packet.ConfigHash != "Capacity".GetHashCode()) return false;

			var capacityControl = go.GetComponent<IUserControlledCapacity>();
			if (capacityControl == null) return false;

			capacityControl.UserMaxCapacity = packet.Value;
			DebugConsole.Log($"[CapacityHandler] Set UserMaxCapacity={packet.Value} on {go.name}");
			return true;
		}
	}
}
