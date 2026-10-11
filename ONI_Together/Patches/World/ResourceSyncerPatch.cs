using HarmonyLib;
using ONI_Together.Networking.Synchronization;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
	/// <summary>
	/// The host counts belong to the world that was loaded when they arrived.
	/// The syncer itself lives on the persistent mod object, see MultiplayerMod.
	/// </summary>
	[HarmonyPatch(typeof(Game), "OnSpawn")]
	public static class GameSpawnPatch
	{
		public static void Postfix()
		{
			using var _ = Profiler.Scope();

			ResourceSyncer.ClearClientCounts();
		}
	}
}
