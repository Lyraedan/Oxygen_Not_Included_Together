using UnityEngine;
using ONI_Together.DebugTools;
using Shared;
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
			NetworkingHash.ForConfigKey("ReceptacleOrder"),
			NetworkingHash.ForConfigKey("ReceptacleCancelRequest"),
			NetworkingHash.ForConfigKey("IncubatorAutoReplace"),
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
				if (hash == NetworkingHash.ForConfigKey("ReceptacleOrder"))
				{
					if (packet.ConfigType != BuildingConfigType.String
					    || string.IsNullOrEmpty(packet.StringValue))
						return false;
					Tag entityTag = new Tag(packet.StringValue);
					Tag filterTag = string.IsNullOrEmpty(packet.SecondaryStringValue)
						? Tag.Invalid
						: new Tag(packet.SecondaryStringValue);
					if (!entityTag.IsValid || !receptacle.HasDepositTag(entityTag))
						return false;
					receptacle.CreateOrder(entityTag, filterTag);
					packet.StringValue = receptacle.requestedEntityTag.Name;
					packet.SecondaryStringValue = receptacle.requestedEntityAdditionalFilterTag.IsValid
						? receptacle.requestedEntityAdditionalFilterTag.Name
						: string.Empty;
					return true;
				}

				if (hash == NetworkingHash.ForConfigKey("ReceptacleCancelRequest"))
				{
					if (packet.ConfigType != BuildingConfigType.Boolean || packet.Value != 1f)
						return false;
					receptacle.CancelActiveRequest();
					return true;
				}
			}

			// Handle EggIncubator auto-replace
			var incubator = go.GetComponent<EggIncubator>();
			if (incubator != null && hash == NetworkingHash.ForConfigKey("IncubatorAutoReplace"))
			{
				if (packet.ConfigType != BuildingConfigType.Boolean
				    || !BuildingConfigPacket.IsBooleanValue(packet.Value))
					return false;
				incubator.autoReplaceEntity = packet.Value > 0.5f;
				//DebugConsole.Log($"[ReceptacleHandler] Set autoReplaceEntity={incubator.autoReplaceEntity} on {go.name}");
				return true;
			}

			return false;
		}
	}
}
