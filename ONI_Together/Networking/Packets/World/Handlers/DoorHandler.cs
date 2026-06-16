using UnityEngine;
using ONI_Together.DebugTools;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>
	/// Handles Door state changes.
	/// </summary>
	public class DoorHandler : IBuildingConfigHandler
	{
		private static readonly int[] _hashes = new int[]
		{
			"DoorState".GetHashCode(),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			if (packet.ConfigHash != "DoorState".GetHashCode()) return false;

			var door = go.GetComponent<Door>();
			if (door == null) return false;

			Door.ControlState state = (Door.ControlState)(int)packet.Value;

			// Skip if already transitioning to this state. Without this check,
			// the host relay (which re-serializes the packet with the host's Sender ID)
			// causes the client to receive its own state change back.
			// QueueStateChange sees requestedState == nextState and takes the cancel
			// path: requestedState = controlState, which resets the door to its old state.
			if (door.RequestedState == state)
				return true;

			door.QueueStateChange(state);
			//DebugConsole.Log($"[DoorHandler] Set DoorState={state} on {go.name}");
			return true;
		}
	}
}
