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
        private static readonly bool ENABLE_LOG = true;

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

        [HarmonyPatch(typeof(Reactable), nameof(Reactable.CanBegin))]
        public static class Reactable_CanBegin_SuitMarker_Patch
        {
            static bool Postfix(bool __result, Reactable __instance, GameObject reactor, Navigator.ActiveTransition transition)
            {
                if (!__instance.gameObject.TryGetComponent<ReactableSyncer>(out var syncer))
                {
                    DebugConsole.LogWarning(
                        $"[ReactablePatch][Postfix]{__instance.gameObject.GetProperName()} Failed to get ReactableSyncer.");

                    return __result;
                }

                if (MultiplayerSession.IsHostInSession && MultiplayerSession.SessionHasPlayers && __result)
                {
                    syncer.RequestSyncAuthorization(__instance, reactor);
                    return __result;
                }

                if (MultiplayerSession.IsClient)
                {
                    bool internalResult = __instance.InternalCanBegin(reactor, transition);
                    return internalResult && syncer.IsAuthorized(__instance, reactor);
                }

                return __result;
            }
        }

        [HarmonyPatch(typeof(Reactable), nameof(Reactable.Begin))]
        public static class Reactable_Begin_Patch
        {
            static void Prefix(Reactable __instance, out AnimSyncer __state,  GameObject reactor)
            {
                string EntityName = __instance.gameObject.GetProperName();
                string ReactableName = __instance.GetType().Name;
                int ReactableId = __instance.id.hash;
                string ReactorName = reactor != null ? reactor.gameObject.GetProperName() : "<unknown reactor>";

                if (ENABLE_LOG)
                {
                    DebugConsole.Log(
                        $"[ReactablePatch][ENTER_BEGIN_PREFIX]" +
                        $"[{EntityName}:<unknown entity netId> " +
                        $"{ReactableName}:{ReactableId} " +
                        $"{ReactorName}:<unknown reactor NetId>, " +
                        $"networkReactionReplayStatic={ReactableSyncer.networkReactionReplayStatic}.");
                }

                __state = null;

                if (reactor == null || !reactor.TryGetComponent<AnimSyncer>(out var animSyncer) || animSyncer == null)
                {
                    // If we got this case, we may need to check the entity's initialization process.
                    DebugConsole.LogWarning($"[ReactablePatch][ENTER_BEGIN_PREFIX] AnimSyncer not found or invalid for reactor: {ReactorName}");
                
                    return;
                }

                __state = animSyncer;
                animSyncer.EnterSyncedPlaybackScope();
                animSyncer.EnterOverrideScope();

                if (ENABLE_LOG)
                {
                    DebugConsole.Log(
                        $"[ReactablePatch][END_BEGIN_PREFIX]" +
                        $"[{EntityName}:<unknown NetId> " +
                        $"{ReactableName}:{ReactableId} " +
                        $"ReactableId:{ReactableId}, " +
                        $"{ReactorName}:{animSyncer?.NetId ?? 0}, " +
                        $"networkReactionReplayStatic={ReactableSyncer.networkReactionReplayStatic}"
                    );
                }
            }

            static void Finalizer(Reactable __instance, Exception __exception, AnimSyncer __state)
            {
                if (__state != null)
                {
                    __state.ExitOverrideScope();
                    __state.ExitSyncedPlaybackScope();
                }
            }
        }

        [HarmonyPatch(typeof(Reactable), nameof(Reactable.End))]
        public static class Reactable_End_Patch
        {
            static void Prefix(Reactable __instance, out AnimSyncer __state)
            {
                __state = null;

                var reactor = __instance.reactor;
                if (reactor == null)
                    return;
                
                string EntityName = __instance.gameObject.GetProperName();
                string ReactableName = __instance.GetType().Name;
                int ReactableId = __instance.id.hash;
                var ReactorName = reactor.GetProperName();

                if (!reactor.TryGetComponent<AnimSyncer>(out var animSyncer) || animSyncer == null)
                {

                    // If we got this case, we may need to check the entity's initialization process.
                    DebugConsole.LogWarning($"[ReactablePatch][ENTER_BEGIN_PREFIX] AnimSyncer not found or invalid for reactor: {ReactorName}");
                
                    return;
                }

                __state = animSyncer;
                animSyncer.EnterSyncedPlaybackScope();
                animSyncer.EnterOverrideScope();

                if (ENABLE_LOG)
                {
                    DebugConsole.Log(
                        $"[ReactablePatch][END_BEGIN_PREFIX]" +
                        $"[{EntityName}:<unknown NetId> " +
                        $"{ReactableName}:{ReactableId} " +
                        $"ReactableId:{ReactableId}, " +
                        $"{ReactorName}:{animSyncer?.NetId ?? 0}, " +
                        $"networkReactionReplayStatic={ReactableSyncer.networkReactionReplayStatic}"
                    );
                }
            }

            static void Finalizer(Reactable __instance, Exception __exception, AnimSyncer __state)
            {
                __state?.ExitOverrideScope();
                __state?.ExitSyncedPlaybackScope();
            }
        }
    }
}
