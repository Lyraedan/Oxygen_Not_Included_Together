using UnityEngine;
using ONI_Together.DebugTools;
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
			"FilterElement".GetHashCode(),
			"FilterTag".GetHashCode(),
			"FilterTagString".GetHashCode(),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			var filterable = go.GetComponent<Filterable>();
			if (filterable == null) return false;

			int hash = packet.ConfigHash;

			if (hash == "FilterElement".GetHashCode())
			{
				SimHashes elementHash = (SimHashes)(int)packet.Value;
				Element element = ElementLoader.FindElementByHash(elementHash);
				if (element != null)
				{
					filterable.SelectedTag = element.tag;
					DebugConsole.Log($"[FilterableHandler] Set FilterElement={element.tag} on {go.name}");
					return true;
				}
			}

			if (hash == "FilterTag".GetHashCode() || hash == "FilterTagString".GetHashCode())
			{
				if (packet.ConfigType == BuildingConfigType.String && !string.IsNullOrEmpty(packet.StringValue))
				{
					Tag tag = new Tag(packet.StringValue);
					filterable.SelectedTag = tag;
					DebugConsole.Log($"[FilterableHandler] Set FilterTag={tag} on {go.name}");
					return true;
				}
			}

			return false;
		}
	}
}
