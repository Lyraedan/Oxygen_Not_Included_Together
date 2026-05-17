using UnityEngine;
using ONI_Together.DebugTools;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>
	/// Handles AccessControl (door permissions) configuration.
	/// Includes default group permissions, per-minion permissions, and robot tag permissions.
	/// </summary>
	public class AccessControlHandler : IBuildingConfigHandler
	{
    private static readonly int[] _hashes = new int[]
		{
			"AccessControlDefault".GetHashCode(),
			"AccessControlMinion".GetHashCode(),
			"AccessControlClear".GetHashCode(),
			"AccessControlRobot".GetHashCode(),
			"AccessControlRobotClear".GetHashCode(),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			int hash = packet.ConfigHash;

			var accessControl = go.GetComponent<AccessControl>();
			if (accessControl == null) return false;

			// Handle default group permission
			if (hash == "AccessControlDefault".GetHashCode())
			{
				if (!string.IsNullOrEmpty(packet.StringValue))
				{
					Tag groupTag = new Tag(packet.StringValue);
					AccessControl.Permission permission = (AccessControl.Permission)(int)packet.Value;
					accessControl.SetDefaultPermission(groupTag, permission);
					DebugConsole.Log($"[AccessControlHandler] Set default permission group={groupTag}, permission={permission} on {go.name}");
					return true;
				}
			}

			// Handle individual minion permission via NetID
			if (hash == "AccessControlMinion".GetHashCode())
			{
				int minionNetId = packet.SliderIndex;
				AccessControl.Permission permission = (AccessControl.Permission)(int)packet.Value;

				// Find the minion by NetID using the registry
				if (NetworkIdentityRegistry.TryGet(minionNetId, out var minionIdentity) && minionIdentity != null)
				{
					var minionGO = minionIdentity.gameObject;
					var minionId = minionGO.GetComponent<MinionIdentity>();
					if (minionId != null && minionId.assignableProxy != null)
					{
						var proxy = minionId.assignableProxy.Get();
						if (proxy != null)
						{
							accessControl.SetPermission(proxy, permission);
							DebugConsole.Log($"[AccessControlHandler] Set minion permission minionNetId={minionNetId}, permission={permission} on {go.name}");
							return true;
						}
					}
				}
				DebugConsole.Log($"[AccessControlHandler] Could not find minion with NetID={minionNetId}");
				return true; // Still return true to prevent "unhandled" warning
			}

			// Handle clear individual minion permission
			if (hash == "AccessControlClear".GetHashCode())
			{
				int minionNetId = packet.SliderIndex;

				if (NetworkIdentityRegistry.TryGet(minionNetId, out var minionIdentity) && minionIdentity != null)
				{
					var minionGO = minionIdentity.gameObject;
					var minionId = minionGO.GetComponent<MinionIdentity>();
					if (minionId != null && minionId.assignableProxy != null)
					{
						var proxy = minionId.assignableProxy.Get();
						if (proxy != null)
						{
							accessControl.ClearPermission(proxy);
							DebugConsole.Log($"[AccessControlHandler] Cleared permission for minionNetId={minionNetId} on {go.name}");
							return true;
						}
					}
				}
				DebugConsole.Log($"[AccessControlHandler] Could not find minion with NetID={minionNetId} for clear");
				return true;
			}

			// Handle robot tag permission (FetchDrone, ScoutRover, MorbRover)
			if (hash == "AccessControlRobot".GetHashCode())
			{
				if (!string.IsNullOrEmpty(packet.StringValue))
				{
					Tag robotTag = new Tag(packet.StringValue);
					AccessControl.Permission permission = (AccessControl.Permission)(int)packet.Value;
					accessControl.SetPermission(robotTag, permission);
					DebugConsole.Log($"[AccessControlHandler] Set robot permission tag={robotTag}, permission={permission} on {go.name}");
					return true;
				}
			}

			// Handle clear robot tag permission
			if (hash == "AccessControlRobotClear".GetHashCode())
			{
				if (!string.IsNullOrEmpty(packet.StringValue))
				{
					Tag robotTag = new Tag(packet.StringValue);
					// Clear robot permission - uses GameTags.Robot as the default key
					accessControl.ClearPermission(robotTag, GameTags.Robot);
					DebugConsole.Log($"[AccessControlHandler] Cleared robot permission for tag={robotTag} on {go.name}");
					return true;
				}
			}

			return false;
		}
	}
}
