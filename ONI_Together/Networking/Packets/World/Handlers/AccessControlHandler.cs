using UnityEngine;
using ONI_Together.DebugTools;
using Shared;
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
			NetworkingHash.ForConfigKey("AccessControlDefault"),
			NetworkingHash.ForConfigKey("AccessControlMinion"),
			NetworkingHash.ForConfigKey("AccessControlClear"),
			NetworkingHash.ForConfigKey("AccessControlRobot"),
			NetworkingHash.ForConfigKey("AccessControlRobotClear"),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			int hash = packet.ConfigHash;

			var accessControl = go.GetComponent<AccessControl>();
			if (accessControl == null) return false;
			bool permissionValid = BuildingConfigPacket.IsIntegralValue(packet.Value)
			                       && System.Enum.IsDefined(
				                       typeof(AccessControl.Permission), (int)packet.Value);

			// Handle default group permission
			if (hash == NetworkingHash.ForConfigKey("AccessControlDefault"))
			{
				if (packet.ConfigType == BuildingConfigType.String && permissionValid
				    && !string.IsNullOrEmpty(packet.StringValue))
				{
					Tag groupTag = new Tag(packet.StringValue);
					AccessControl.Permission permission = (AccessControl.Permission)(int)packet.Value;
					accessControl.SetDefaultPermission(groupTag, permission);
					//DebugConsole.Log($"[AccessControlHandler] Set default permission group={groupTag}, permission={permission} on {go.name}");
					return true;
				}
			}

			// Handle individual minion permission via NetID
			if (hash == NetworkingHash.ForConfigKey("AccessControlMinion"))
			{
				if (!permissionValid)
					return false;
				int minionNetId = packet.ReferenceNetId;
				if (minionNetId == 0)
					return false;
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
							//DebugConsole.Log($"[AccessControlHandler] Set minion permission minionNetId={minionNetId}, permission={permission} on {go.name}");
							return true;
						}
					}
				}
				//DebugConsole.Log($"[AccessControlHandler] Could not find minion with NetID={minionNetId}");
				return false;
			}

			// Handle clear individual minion permission
			if (hash == NetworkingHash.ForConfigKey("AccessControlClear"))
			{
				int minionNetId = packet.ReferenceNetId;
				if (minionNetId == 0)
					return false;

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
							//DebugConsole.Log($"[AccessControlHandler] Cleared permission for minionNetId={minionNetId} on {go.name}");
							return true;
						}
					}
				}
				//DebugConsole.Log($"[AccessControlHandler] Could not find minion with NetID={minionNetId} for clear");
				return false;
			}

			// Handle robot tag permission (FetchDrone, ScoutRover, MorbRover)
			if (hash == NetworkingHash.ForConfigKey("AccessControlRobot"))
			{
				if (packet.ConfigType == BuildingConfigType.String && permissionValid
				    && !string.IsNullOrEmpty(packet.StringValue))
				{
					Tag robotTag = new Tag(packet.StringValue);
					AccessControl.Permission permission = (AccessControl.Permission)(int)packet.Value;
					accessControl.SetPermission(robotTag, permission);
					//DebugConsole.Log($"[AccessControlHandler] Set robot permission tag={robotTag}, permission={permission} on {go.name}");
					return true;
				}
			}

			// Handle clear robot tag permission
			if (hash == NetworkingHash.ForConfigKey("AccessControlRobotClear"))
			{
				if (packet.ConfigType == BuildingConfigType.String
				    && !string.IsNullOrEmpty(packet.StringValue))
				{
					Tag robotTag = new Tag(packet.StringValue);
					// Clear robot permission - uses GameTags.Robot as the default key
					accessControl.ClearPermission(robotTag, GameTags.Robot);
					//DebugConsole.Log($"[AccessControlHandler] Cleared robot permission for tag={robotTag} on {go.name}");
					return true;
				}
			}

			return false;
		}
	}
}
