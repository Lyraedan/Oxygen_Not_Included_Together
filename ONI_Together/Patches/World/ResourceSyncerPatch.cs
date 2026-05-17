using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Synchronization;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
	// Attach ResourceSyncer to the Game or World object
	[HarmonyPatch(typeof(Game), "OnSpawn")]
	public static class GameSpawnPatch
	{
		public static void Postfix(Game __instance)
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
			{
				// Attach to Game.Instance.gameObject (Global helper)
				var syncer = __instance.gameObject.GetComponent<ResourceSyncer>();
				if (syncer == null)
				{
					__instance.gameObject.AddComponent<ResourceSyncer>();
				}
			}
			else
			{
				// Client: Clear stale resources
				ResourceSyncer.ClientResources.Clear();
			}
		}
	}
}
