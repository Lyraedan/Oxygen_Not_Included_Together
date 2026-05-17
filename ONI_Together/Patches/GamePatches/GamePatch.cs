using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Misc.World;
using ONI_Together.Networking;
using ONI_Together.Networking.States;
using Shared.Profiling;

namespace ONI_Together.Patches.GamePatches
{
  /// <summary>
  /// Patch Game.Update to run the two batchers if host
  /// </summary>
  [HarmonyPatch(typeof(Game), "Update")]
  public static class GameUpdatePatch
  {
    public static void Postfix()
    {
      using var _ = Profiler.Scope();

      if (MultiplayerSession.IsHost)
      {
        InstantiationBatcher.Update();
        WorldUpdateBatcher.Update();
      }
    }
  }

  /// <summary>
  /// Patch Game.OnSpawn to handle client reconnection after world load
  /// </summary>
  [HarmonyPatch(typeof(Game), "OnSpawn")]
  public static class GameOnSpawnPatch
  {
    public static void Postfix()
    {
      using var _ = Profiler.Scope();

      DebugConsole.Log($"[GamePatch] Game.OnSpawn fired. ClientState={GameClient.State}, HasCachedConnection={GameClient.HasCachedConnection()}, IsHost={MultiplayerSession.IsHost}, HardSync={GameClient.IsHardSyncInProgress}");

      // Handle client reconnection after world is fully loaded
      // This is triggered AFTER the game world is completely initialized,
      // which is much safer than OnPostSceneLoaded which fires during unload

      // Check if we have cached connection info waiting to reconnect
      // Note: Can't use IsClient here because InSession is false after disconnect
      // Instead check for cached connection and ensure we're not the host
      if (GameClient.HasCachedConnection() && !MultiplayerSession.IsHost)
      {
        DebugConsole.Log("[GamePatch] World fully loaded, reconnecting to host from cache...");
        GameClient.ReconnectFromCache();
        MultiplayerOverlay.Close();
      }
    }
  }
}
