using UnityEngine;
using ONI_Together.DebugTools;
using Shared;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>
	/// Handles Filterable buildings (gas/liquid filters and element sensors).
	/// </summary>
	public class FilterableHandler : IBuildingConfigHandler
	{
		private static readonly int[] _hashes = new int[]
		{
			NetworkingHash.ForConfigKey("FilterElement"),
			NetworkingHash.ForConfigKey("FilterTag"),
			NetworkingHash.ForConfigKey("FilterTagString"),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			var filterable = go.GetComponent<Filterable>();
			if (filterable == null) return false;

			int hash = packet.ConfigHash;

			if (hash == NetworkingHash.ForConfigKey("FilterElement"))
			{
				if (packet.ConfigType != BuildingConfigType.Float
				    || !BuildingConfigPacket.IsIntegralValue(packet.Value))
					return false;
				SimHashes elementHash = (SimHashes)(int)packet.Value;
				Element element = ElementLoader.FindElementByHash(elementHash);
				if (element != null && IsAllowedTag(filterable, element.tag))
				{
					filterable.SelectedTag = element.tag;
					//DebugConsole.Log($"[FilterableHandler] Set FilterElement={element.tag} on {go.name}");
					return true;
				}
			}

			if (hash == NetworkingHash.ForConfigKey("FilterTag") || hash == NetworkingHash.ForConfigKey("FilterTagString"))
			{
				if (packet.ConfigType == BuildingConfigType.String && !string.IsNullOrEmpty(packet.StringValue))
				{
					Tag tag = new Tag(packet.StringValue);
					if (!IsAllowedTag(filterable, tag))
						return false;
					filterable.SelectedTag = tag;
					packet.StringValue = filterable.SelectedTag.Name;
					//DebugConsole.Log($"[FilterableHandler] Set FilterTag={tag} on {go.name}");
					return true;
				}
			}

			return false;
		}

		private static bool IsAllowedTag(Filterable filterable, Tag tag)
		{
			if (!tag.IsValid) return false;
			foreach (var group in filterable.GetTagOptions())
			{
				if (group.Key == tag || group.Value.Contains(tag))
					return true;
			}
			return false;
		}
	}
}
