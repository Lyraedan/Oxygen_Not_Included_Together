using HarmonyLib;

namespace ONI_Together.Patches
{
	[HarmonyPatch(typeof(Pathfinding), nameof(Pathfinding.UpdateNavGrids))]
	public static class Pathfinding_UpdateNavGrids_Patch
	{
		public static bool Prefix(ref bool update_all)
		{
			/* Old code, replaced with navigator patch
			if (!MultiplayerSession.IsHost)
			{
					//DebugConsole.LogError("We are not the host!");
					return false; // Skip updating navgrids for clients
			}*/

			return true; // Allow host to run original method
		}
	}

}
