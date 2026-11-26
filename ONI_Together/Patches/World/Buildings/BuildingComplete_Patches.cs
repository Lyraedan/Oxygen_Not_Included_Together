using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Tools.Deconstruct;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace ONI_Together.Patches.World.Buildings
{
	internal class BuildingComplete_Patches
	{

        [HarmonyPatch(typeof(BuildingComplete), nameof(BuildingComplete.OnPrefabInit))]
        public class BuildingComplete_OnPrefabInit_Patch
        {
            public static void Postfix(BuildingComplete __instance)
            {
                using var _ = Profiler.Scope();
                try
                {
                    __instance.gameObject.AddOrGet<NetworkIdentity>();

                    if (AnimSyncEligibility.IsAnimatedBuilding(__instance.gameObject))
                        __instance.gameObject.AddOrGet<AnimStateSyncer>();
                }
                catch (System.Exception ex)
                {
                    DebugConsole.LogError($"[BuildingComplete_OnPrefabInit_Patch] {ex}");
                }
            }
        }

        /// <summary>
        /// Ensures buildings destroyed on the host by overheating, damage, melting,
        /// sandbox delete, or replacement are also removed on clients (not just normal deconstruction).
        /// </summary>
        [HarmonyPatch(typeof(BuildingComplete), nameof(BuildingComplete.OnCleanUp))]
        public class BuildingComplete_OnCleanUp_Patch
        {
            public static void Prefix(BuildingComplete __instance)
            {
                using var _ = Profiler.Scope();
                try
                {
                    if (!MultiplayerSession.IsHostInSession || !MultiplayerSession.SessionHasPlayers)
                        return;
                    if (App.IsExiting || Game.IsQuitting() || GameServerHardSync.IsHardSyncInProgress)
                        return;
                    if (__instance == null || __instance.Def == null)
                        return;

                    int cell = Grid.PosToCell(__instance.gameObject);
                    if (!Grid.IsValidCell(cell))
                        return;

                    PacketSender.SendToAllClients(new DeconstructCompletePacket
                    {
                        Cell = cell,
                        ObjectLayer = (int)__instance.Def.ObjectLayer
                    }, PacketSendMode.Reliable);
                }
                catch (System.Exception ex)
                {
                    DebugConsole.LogError($"[BuildingComplete_OnCleanUp_Patch] {ex}");
                }
            }
        }
	}
}
