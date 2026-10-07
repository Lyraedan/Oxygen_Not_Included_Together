using HarmonyLib;
using ONI_Together.Menus;
using ONI_Together.Misc.World;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Components.Tools;
using ONI_Together.UI;
using Shared.Profiling;

namespace ONI_Together.Patches.GamePatches
{

    /// <summary>
    /// General cleanup patch
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.OnLoadLevel))]
    public class Game_OnLoadLevel_Patch
    {
        public static void Postfix()
        {
            UnityChatBoxUI.DestroyInstance();
		}
    }

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
  /// Complete client synchronization after the loaded world finishes spawning
  /// </summary>
  [HarmonyPatch(typeof(Game), nameof(Game.OnSpawn))]
  public static class GameOnSpawnPatch
  {
    public static void Postfix()
    {
      using var _ = Profiler.Scope();

      Game.Instance.gameObject.AddComponent<LogicPortManager>();

      MoveToLocationToolSyncer.RegisterNetId(Game.Instance.gameObject);

      Game.Instance.OnSpawnComplete += OnSpawnComplete;
    }

    private static void OnSpawnComplete()
    {
      using var _ = Profiler.Scope();

      if (MultiplayerSession.IsHost)
        return;

      GameClient.OnWorldSpawnComplete();
    }
  }
}
