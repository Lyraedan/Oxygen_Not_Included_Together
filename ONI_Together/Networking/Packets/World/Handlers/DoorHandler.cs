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
			"DoorStateComplete".GetHashCode(),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			bool isStateChange = packet.ConfigHash == "DoorState".GetHashCode();
			bool isStateComplete = packet.ConfigHash == "DoorStateComplete".GetHashCode();
			if (!isStateChange && !isStateComplete) return false;

			var door = go.GetComponent<Door>();
			if (door == null) return false;

			Door.ControlState state = (Door.ControlState)(int)packet.Value;

			if (isStateComplete)
			{
				// Host finished the door work chore: cancel any lingering client chore,
				// clear the pending status icon, and apply the final control state directly.
				if (door.changeStateChore != null)
				{
					door.changeStateChore.Cancel("Host completed door state change");
					door.changeStateChore = null;
				}
				door.requestedState = state;
				door.controlState = state;
				door.RefreshControlState();
				door.OnOperationalChanged(null);
				var selectable = door.GetComponent<KSelectable>();
				if (selectable != null)
				{
					selectable.RemoveStatusItem(Db.Get().BuildingStatusItems.ChangeDoorControlState);
				}
				return true;
			}

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
