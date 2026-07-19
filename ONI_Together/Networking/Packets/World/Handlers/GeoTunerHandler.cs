using UnityEngine;
using ONI_Together.DebugTools;
using Shared;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>
	/// Handles GeoTuner (Geyser Tuner) buildings.
	/// </summary>
	public class GeoTunerHandler : IBuildingConfigHandler
	{
		private static readonly int[] _hashes = new int[]
		{
			NetworkingHash.ForConfigKey("GeoTunerGeyser"),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			if (packet.ConfigHash != NetworkingHash.ForConfigKey("GeoTunerGeyser")) return false;

			var geoTuner = go.GetSMI<GeoTuner.Instance>();
			if (geoTuner == null) return false;
			if (packet.ConfigType != BuildingConfigType.Float
			    || !BuildingConfigPacket.IsIntegralValue(packet.Value))
				return false;

			int geyserCell = (int)packet.Value;
			Geyser targetGeyser = null;

			if (geyserCell >= 0 && Grid.IsValidCell(geyserCell))
			{
				// Find the geyser at the specified cell
				foreach (var geyser in global::Components.Geysers.GetItems(geoTuner.GetMyWorldId()))
				{
					if (Grid.PosToCell(geyser.gameObject) == geyserCell)
					{
						targetGeyser = geyser;
						break;
					}
				}
			}
			if (geyserCell >= 0 && targetGeyser == null)
				return false;

			geoTuner.AssignFutureGeyser(targetGeyser);
			//DebugConsole.Log($"[GeoTunerHandler] Set geyser to {targetGeyser?.name ?? "null"} on {go.name}");
			return true;
		}
	}
}
