using UnityEngine;

namespace ONI_Together.Networking.Components
{
	internal static class AnimSyncEligibility
	{
		internal static bool IsAnimatedCritter(GameObject go)
		{
			return go != null
				&& go.HasTag(GameTags.Creature)
				&& !go.HasTag(GameTags.BaseMinion)
				&& go.GetComponent<KBatchedAnimController>() != null;
		}

		internal static bool IsAnimatedBuilding(GameObject go)
		{
			if (go == null
				|| go.GetComponent<BuildingComplete>() == null
				|| go.GetComponent<KBatchedAnimController>() == null)
			{
				return false;
			}

			// Limit building sync to components with visible state-driven animation changes.
			return go.GetComponent<Operational>() != null
				|| go.GetComponent<Door>() != null
				|| go.GetComponent<ComplexFabricator>() != null
				|| go.GetComponent<IHaveUtilityNetworkMgr>() != null
				|| go.GetComponent<KAnimGraphTileVisualizer>() != null;
		}

		internal static bool IsAnimatedNonMinion(GameObject go)
		{
			return IsAnimatedCritter(go) || IsAnimatedBuilding(go);
		}
	}
}
