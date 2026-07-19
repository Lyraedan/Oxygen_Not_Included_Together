using UnityEngine;
using ONI_Together.DebugTools;
using Shared;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>
	/// Handles ComplexFabricator recipe queue sync for crafting stations.
	/// Uses ConfigType.RecipeQueue where ConfigHash = NetworkingHash.ForConfigKey(recipe.id) and Value = count.
	/// </summary>
	public class CraftingHandler : IBuildingConfigHandler
	{
		// This handler doesn't use specific hashes - it matches on ConfigType.RecipeQueue
		// and uses the recipe ID hash dynamically
		private static readonly int[] _hashes = new int[0];

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			// Only handle RecipeQueue config type
			if (packet.ConfigType != BuildingConfigType.RecipeQueue)
				return false;
			if (!BuildingConfigPacket.IsIntegralValue(packet.Value)
			    || !BuildingConfigPacket.IsInRange(packet.Value, 0f, 9999f))
				return false;

			var fabricator = go.GetComponent<ComplexFabricator>();
			if (fabricator == null) return false;

			int targetRecipeHash = packet.ConfigHash;
			int count = (int)packet.Value;

			// Find the matching recipe in the available recipes
			foreach (var recipe in fabricator.GetRecipes())
			{
				if (NetworkingHash.ForConfigKey(recipe.id) == targetRecipeHash)
				{
					fabricator.SetRecipeQueueCount(recipe, count);
					//DebugConsole.Log($"[CraftingHandler] Set recipe '{recipe.id}' count={count} on {go.name}");
					return true;
				}
			}

			DebugConsole.LogWarning($"[CraftingHandler] Recipe not found for hash {targetRecipeHash} on {go.name}");
			return false;
		}
	}
}
