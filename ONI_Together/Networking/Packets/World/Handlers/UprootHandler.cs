using UnityEngine;
using ONI_Together.DebugTools;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	public class UprootHandler : IBuildingConfigHandler
	{
		private static readonly int[] _hashes = new int[]
		{
			"UprootPlant".GetHashCode(),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			int hash = packet.ConfigHash;

			// The packet uses Cell to find the plant since plants don't have NetworkIdentity on the building
			// We need to find the Uprootable at the cell
			if (!Grid.IsValidCell(packet.Cell)) return false;

			// Find the plant at the cell - check the go first, then search grid objects
			Uprootable uprootable = go.GetComponent<Uprootable>();

			if (uprootable == null)
			{
				// Search for the plant entity at this cell
				// Plants can be on different object layers
				for (int layer = 0; layer < (int)ObjectLayer.NumLayers; layer++)
				{
					var obj = Grid.Objects[packet.Cell, layer];
					if (obj != null)
					{
						uprootable = obj.GetComponent<Uprootable>();
						if (uprootable != null) break;
					}
				}
			}

			if (uprootable == null) return false;

			if (hash == "UprootPlant".GetHashCode())
			{
				uprootable.MarkForUproot();
				DebugConsole.Log($"[UprootHandler] Marked plant for uproot at cell {packet.Cell}");
				return true;
			}

			return false;
		}
	}
}
