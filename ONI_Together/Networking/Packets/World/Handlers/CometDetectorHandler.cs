using UnityEngine;
using ONI_Together.DebugTools;
using Shared;
using Shared.Profiling;
using ONI_Together.Networking.Components;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>
	/// Handles Comet Detector (Space Scanner) configuration.
	/// Supports both DLC (ClusterCometDetector) and base game (CometDetector).
	/// </summary>
	public class CometDetectorHandler : IBuildingConfigHandler
	{
		private static readonly int[] _hashes = new int[]
		{
			NetworkingHash.ForConfigKey("ClusterCometDetectorState"),
			NetworkingHash.ForConfigKey("ClusterCometDetectorTarget"),
			NetworkingHash.ForConfigKey("CometDetectorTarget"),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			int hash = packet.ConfigHash;

			// ==================== DLC (Spaced Out) ====================

			// ClusterCometDetector state (meteors, ballistic, rocket)
			if (hash == NetworkingHash.ForConfigKey("ClusterCometDetectorState"))
			{
				if (packet.ConfigType != BuildingConfigType.Float
				    || !BuildingConfigPacket.IsIntegralValue(packet.Value)
				    || !System.Enum.IsDefined(
					    typeof(ClusterCometDetector.Instance.ClusterCometDetectorState),
					    (int)packet.Value))
					return false;
				var clusterDetector = go.GetSMI<ClusterCometDetector.Instance>();
				if (clusterDetector != null)
				{
					var state = (ClusterCometDetector.Instance.ClusterCometDetectorState)(int)packet.Value;
					clusterDetector.SetDetectorState(state);
					//DebugConsole.Log($"[CometDetectorHandler] Set ClusterCometDetector state={state} on {go.name}");
					return true;
				}
			}

			// ClusterCometDetector target (which rocket to track)
			if (hash == NetworkingHash.ForConfigKey("ClusterCometDetectorTarget"))
			{
				var clusterDetector = go.GetSMI<ClusterCometDetector.Instance>();
				if (clusterDetector != null)
				{
					int targetNetId = packet.ReferenceNetId;
					Clustercraft targetCraft = null;

					if (targetNetId != 0)
					{
						// Find the clustercraft by NetId
						if (!TryResolveClustercraft(targetNetId, out targetCraft))
							return false;
					}

					clusterDetector.SetClustercraftTarget(targetCraft);
					//DebugConsole.Log($"[CometDetectorHandler] Set ClusterCometDetector target={targetCraft?.Name ?? "null"} on {go.name}");
					return true;
				}
			}

			// ==================== Base Game ====================

			// CometDetector target craft
			if (hash == NetworkingHash.ForConfigKey("CometDetectorTarget"))
			{
				var detector = go.GetSMI<CometDetector.Instance>();
				if (detector != null)
				{
					int targetNetId = packet.ReferenceNetId;
					LaunchConditionManager targetCraft = null;

					if (targetNetId != 0)
					{
						// Find the launch condition manager by NetId
						if (!NetworkIdentityRegistry.TryGet(targetNetId, out var targetIdentity)
						    || targetIdentity == null
						    || (targetCraft = targetIdentity.gameObject.GetComponent<LaunchConditionManager>()) == null)
							return false;
					}

					detector.SetTargetCraft(targetCraft);
					//DebugConsole.Log($"[CometDetectorHandler] Set CometDetector target NetId={targetNetId} on {go.name}");
					return true;
				}
			}

			return false;
		}

		private static bool TryResolveClustercraft(int netId, out Clustercraft craft)
		{
			craft = null;
			if (!NetworkIdentityRegistry.TryGet(netId, out NetworkIdentity identity) || identity == null)
				return false;
			craft = identity.gameObject.GetComponent<Clustercraft>();
			if (craft != null) return true;
			craft = identity.gameObject.GetComponent<RocketModuleCluster>()
				?.CraftInterface?.GetComponent<Clustercraft>();
			return craft != null;
		}
	}
}
