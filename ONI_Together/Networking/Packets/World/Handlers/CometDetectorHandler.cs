using UnityEngine;
using ONI_Together.DebugTools;
using Shared.Profiling;

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
			"ClusterCometDetectorState".GetHashCode(),
			"ClusterCometDetectorTarget".GetHashCode(),
			"CometDetectorTarget".GetHashCode(),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			int hash = packet.ConfigHash;

			// ==================== DLC (Spaced Out) ====================

			// ClusterCometDetector state (meteors, ballistic, rocket)
			if (hash == "ClusterCometDetectorState".GetHashCode())
			{
				var clusterDetector = go.GetSMI<ClusterCometDetector.Instance>();
				if (clusterDetector != null)
				{
					var state = (ClusterCometDetector.Instance.ClusterCometDetectorState)(int)packet.Value;
					clusterDetector.SetDetectorState(state);
					DebugConsole.Log($"[CometDetectorHandler] Set ClusterCometDetector state={state} on {go.name}");
					return true;
				}
			}

			// ClusterCometDetector target (which rocket to track)
			if (hash == "ClusterCometDetectorTarget".GetHashCode())
			{
				var clusterDetector = go.GetSMI<ClusterCometDetector.Instance>();
				if (clusterDetector != null)
				{
					int targetNetId = packet.SliderIndex;
					Clustercraft targetCraft = null;

					if (targetNetId != -1)
					{
						// Find the clustercraft by NetId
						if (NetworkIdentityRegistry.TryGet(targetNetId, out var targetIdentity) && targetIdentity != null)
						{
							targetCraft = targetIdentity.gameObject.GetComponent<Clustercraft>();
						}
					}

					clusterDetector.SetClustercraftTarget(targetCraft);
					DebugConsole.Log($"[CometDetectorHandler] Set ClusterCometDetector target={targetCraft?.Name ?? "null"} on {go.name}");
					return true;
				}
			}

			// ==================== Base Game ====================

			// CometDetector target craft
			if (hash == "CometDetectorTarget".GetHashCode())
			{
				var detector = go.GetSMI<CometDetector.Instance>();
				if (detector != null)
				{
					int targetNetId = packet.SliderIndex;
					LaunchConditionManager targetCraft = null;

					if (targetNetId != -1)
					{
						// Find the launch condition manager by NetId
						if (NetworkIdentityRegistry.TryGet(targetNetId, out var targetIdentity) && targetIdentity != null)
						{
							targetCraft = targetIdentity.gameObject.GetComponent<LaunchConditionManager>();
						}
					}

					detector.SetTargetCraft(targetCraft);
					DebugConsole.Log($"[CometDetectorHandler] Set CometDetector target NetId={targetNetId} on {go.name}");
					return true;
				}
			}

			return false;
		}
	}
}
