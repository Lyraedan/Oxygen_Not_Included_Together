using System;
using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Components.Entities;
using UnityEngine;

namespace ONI_Together.Patches.Entities
{
    class Reactable_Patches
    {
        private static readonly bool ENABLE_LOG = false;

        [HarmonyPatch(typeof(Reactable), nameof(Reactable.Initialize))]
        public static class Reactable_Initialize_Patch
        {
            static void Postfix(Reactable __instance)
            {
                if (__instance == null || __instance.IsNullOrDestroyed())
                    return;

                Type type = __instance.GetType();

                if (__instance.gameObject == null || __instance.gameObject.IsNullOrDestroyed())
                {
                    DebugConsole.LogWarning(
                        $"[ReactableSyncer][PATCH_INITIALIZE]{type.Name}:{type.FullName.GetHashCode()} " +
                        $"Initialization failed due to missing or destroyed GameObject."
                    );
                    return;
                }
                
                ReactableSyncer syncer = __instance.gameObject.AddOrGet<ReactableSyncer>();
                syncer.RegisterReactable(__instance);
            }
        }


        [HarmonyPatch(typeof(Reactable), nameof(Reactable.Begin))]
        public static class Reactable_Begin_Patch
        {
            static void Prefix(Reactable __instance, out AnimSyncer __state,  GameObject reactor)
            {
                __state = null;
                DebugConsole.Log($"[WE_NEED_THIS][PATCH_BEGIN]{__instance.GetType().Name}:{__instance.GetType().FullName.GetHashCode()} Attempting to sync reactable.");
                if(!CanSyncReactable(false, __instance, reactor, out var reactableSyncer, out var animSyncer))
                    return;

                if (animSyncer != null)
                {
                    __state = animSyncer;
                    animSyncer.EnterSyncedPlaybackScope();
                    animSyncer.EnterOverrideScope();
                }

                reactableSyncer?.RequestSyncReactable(__instance, animSyncer.NetId, animSyncer.EntityName);
            }
        }

        [HarmonyPatch(typeof(Reactable), nameof(Reactable.Begin))]
        public static class Reactable_Begin_Postfix_Patch
        {
            static void Postfix(Reactable __instance, AnimSyncer __state, GameObject reactor)
            {
                if (__state != null)
                {
                    __state.EnterSyncedPlaybackScope();
                    __state.EnterOverrideScope();
                }
            }
        }

        [HarmonyPatch(typeof(Reactable), nameof(Reactable.End))]
        public static class Reactable_End_Patch
        {
            static void Prefix(Reactable __instance, out AnimSyncer __state)
            {
                __state = null;
                if (!CanSyncReactable(true, __instance, __instance.reactor, out var _, out var animSyncer) || animSyncer == null)
                    return;

                __state = animSyncer;
            }
        }
        internal static bool CanSyncReactable(bool isEnd, Reactable reactable, GameObject reactor, out ReactableSyncer reactableSyncer, out AnimSyncer animSyncer)
        {
            reactableSyncer = null;
            animSyncer = null;

            if (!MultiplayerSession.InActiveSession || (MultiplayerSession.IsHost && !MultiplayerSession.SessionHasPlayers))
                return false;

            if (reactable == null || reactable.IsNullOrDestroyed())
                return false;
            
            string reactableName = reactable.GetType().Name;
            int reactableNameHash = reactable.GetType().FullName.GetHashCode();

            string EntityName = reactable.gameObject.GetProperName();

            if (!reactable.gameObject.TryGetComponent<ReactableSyncer>(out var _reactableSyncer) || _reactableSyncer == null)
            {
                // The initializer is on the top of this file.
                // We should attached one ReactableSyncer before any component tries to use it.
                DebugConsole.LogWarning(
                    $"[ReactableSyncer][PATCH_CAN_SYNC]{EntityName}:<unknown NetId> " +
                    $"{reactableName}:{reactableNameHash} ReactableSyncer not found or invalid."
                );
                return false;
            }
            
            if (reactor == null)
                reactor = reactable.reactor;
            if ((reactor == null || reactor.IsNullOrDestroyed()) && !isEnd)
            {
                DebugConsole.LogWarning(_reactableSyncer.GetLogStr(
                    "PATCH_CAN_SYNC",
                    reactableName, reactableNameHash,
                    "<unknown reactor>", 0,
                    Time.unscaledTime, "Reactor not found or invalid."
                ));

                return false;
            }

            if (isEnd)
            {
                DebugConsole.LogWarning(_reactableSyncer.GetLogStr(
                    "PATCH_CAN_SYNC",
                    reactableName, reactableNameHash,
                    "<unknown reactor>", 0,
                    Time.unscaledTime, "Successfully validated."
                ));

                return true;
            }

            var reactorName = reactor.GetProperName();

            if (!reactor.TryGetComponent<NetworkIdentity>(out var identity) || identity == null || identity.NetId == 0)
            {
                // If we got this case, we may need to check the entity's initialization process.
                DebugConsole.LogWarning(_reactableSyncer.GetLogStr(
                    "PATCH_CAN_SYNC",
                    reactableName, reactableNameHash,
                    reactorName, 0,
                    Time.unscaledTime, "NetworkIdentity not found or invalid."
                ));
                return false;
            }

            if (!reactor.TryGetComponent<AnimSyncer>(out var _syncer) || _syncer == null)
            {
                // If we got this case, we may need to check the entity's initialization process.
                DebugConsole.LogWarning(_reactableSyncer.GetLogStr(
                    "PATCH_CAN_SYNC",
                    reactableName, reactableNameHash,
                    reactorName, identity.NetId,
                    Time.unscaledTime, "AnimSyncer not found or invalid."
                ));
                return false;
            }

            if (ENABLE_LOG)
                DebugConsole.LogNonImportant(_reactableSyncer.GetLogStr(
                    "PATCH_CAN_SYNC",
                    reactableName, reactableNameHash,
                    reactorName, identity.NetId,
                    Time.unscaledTime, "Successfully validated."
                ));

            animSyncer = _syncer;
            
            if (MultiplayerSession.IsHost)
                reactableSyncer = _reactableSyncer;

            return true;
        }
    }
}
