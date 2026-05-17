using UnityEngine;
using ONI_Together.DebugTools;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>
	/// Handles SingleEntityReceptacle buildings (planters, incubators, etc.).
	/// </summary>
	public class ReceptacleHandler : IBuildingConfigHandler
	{
		private static readonly int[] _hashes = new int[]
		{
			"ReceptacleEntityTag".GetHashCode(),
			"ReceptacleFilterTag".GetHashCode(),
			"ReceptacleCancelRequest".GetHashCode(),
			"IncubatorAutoReplace".GetHashCode(),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			int hash = packet.ConfigHash;

			// Handle SingleEntityReceptacle
			var receptacle = go.GetComponent<SingleEntityReceptacle>();
			if (receptacle != null)
			{
				if (hash == "ReceptacleEntityTag".GetHashCode())
				{
					if (packet.ConfigType == BuildingConfigType.String)
					{
						Tag entityTag = string.IsNullOrEmpty(packet.StringValue) ? Tag.Invalid : new Tag(packet.StringValue);
						if (entityTag.IsValid)
						{
							receptacle.CreateOrder(entityTag, Tag.Invalid);
							DebugConsole.Log($"[ReceptacleHandler] Created order for {entityTag} on {go.name}");
						}
						else
						{
							receptacle.CancelActiveRequest();
							DebugConsole.Log($"[ReceptacleHandler] Cancelled order on {go.name}");
						}
						return true;
					}
				}

				if (hash == "ReceptacleFilterTag".GetHashCode())
				{
					// Additional filter tag (for mutations, etc.) is handled together with entity tag
					return true;
				}

				if (hash == "ReceptacleCancelRequest".GetHashCode())
				{
					receptacle.CancelActiveRequest();
					DebugConsole.Log($"[ReceptacleHandler] Cancelled request on {go.name}");
					return true;
				}
			}

			// Handle EggIncubator auto-replace
			var incubator = go.GetComponent<EggIncubator>();
			if (incubator != null && hash == "IncubatorAutoReplace".GetHashCode())
			{
				incubator.autoReplaceEntity = packet.Value > 0.5f;
				DebugConsole.Log($"[ReceptacleHandler] Set autoReplaceEntity={incubator.autoReplaceEntity} on {go.name}");
				return true;
			}

			return false;
		}
	}
}
