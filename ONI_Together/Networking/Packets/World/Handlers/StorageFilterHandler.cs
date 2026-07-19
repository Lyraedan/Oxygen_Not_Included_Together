using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using ONI_Together.DebugTools;
using Shared;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>
	/// Handles TreeFilterable buildings (storage bins, refrigerators, critter buildings).
	/// </summary>
	public class StorageFilterHandler : IBuildingConfigHandler
	{
		private static readonly int[] _hashes = new int[]
		{
			NetworkingHash.ForConfigKey("StorageFilterAdd"),
			NetworkingHash.ForConfigKey("StorageFilterRemove"),
			NetworkingHash.ForConfigKey("StorageFilterSet"),
			NetworkingHash.ForConfigKey("StorageSweepOnly"),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			int hash = packet.ConfigHash;

			// Handle TreeFilterable
			var treeFilterable = go.GetComponent<TreeFilterable>();
			if (treeFilterable != null)
			{
				if (hash == NetworkingHash.ForConfigKey("StorageFilterAdd"))
				{
					if (packet.ConfigType == BuildingConfigType.String && !string.IsNullOrEmpty(packet.StringValue))
					{
						Tag tag = new Tag(packet.StringValue);
						if (!IsAllowedTag(treeFilterable, tag))
							return false;
						treeFilterable.AddTagToFilter(tag);
						packet.StringValue = tag.Name;
						//DebugConsole.Log($"[StorageFilterHandler] Added filter tag {tag} on {go.name}");
						return true;
					}
				}

				if (hash == NetworkingHash.ForConfigKey("StorageFilterRemove"))
				{
					if (packet.ConfigType == BuildingConfigType.String && !string.IsNullOrEmpty(packet.StringValue))
					{
						Tag tag = new Tag(packet.StringValue);
						if (!tag.IsValid || !treeFilterable.ContainsTag(tag))
							return false;
						treeFilterable.RemoveTagFromFilter(tag);
						packet.StringValue = tag.Name;
						//DebugConsole.Log($"[StorageFilterHandler] Removed filter tag {tag} on {go.name}");
						return true;
					}
				}
			}

			// Handle Storage sweep-only
			var storage = go.GetComponent<Storage>();
			if (storage != null && hash == NetworkingHash.ForConfigKey("StorageSweepOnly"))
			{
				if (packet.ConfigType != BuildingConfigType.Boolean
				    || !BuildingConfigPacket.IsBooleanValue(packet.Value))
					return false;
				storage.SetOnlyFetchMarkedItems(packet.Value > 0.5f);
				//DebugConsole.Log($"[StorageFilterHandler] Set SweepOnly={packet.Value > 0.5f} on {go.name}");
				return true;
			}

			return false;
		}

		private static bool IsAllowedTag(TreeFilterable treeFilterable, Tag tag)
		{
			if (!tag.IsValid || treeFilterable.ForbiddenTags.Contains(tag))
				return false;

			return treeFilterable.GetFilterStorage().storageFilters.Any(category =>
				DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(category).Contains(tag));
		}
	}
}
