using ONI_Together.DebugTools;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using Shared.Profiling;
using UnityEngine;
using System;
using System.Collections.Generic;
using ONI_Together.Networking.Components;

namespace ONI_Together.Networking.OxySync.Components.Entities
{
    [FixedInterestGroup]
	public class ReactableSyncer : NetworkBehaviour
	{
        // Logging flag for debugging
        private static readonly bool ENABLE_LOG = false;

        private readonly Dictionary<int, (string, Reactable)> reactables = new();
        private readonly Dictionary<int, float> reactableUpdateTimes = new();

        private string EntityName => gameObject?.GetProperName() ?? "Unknown Entity";

        public override void OnPrefabInit()
        {
            base.OnPrefabInit();

            // Minions and the creatures should and can move outside the view.
            // To ensure that the world is consistent,
            // sync the reactable so the entity's state remains consistent.
            // For exampple, gas masks station and checkpoints would need to update their remaining gas and equiment,
            // whenever a duplicant takes a mask out or put one back.
            InterestGroup = -1;
			
            reactables.Clear();
            reactableUpdateTimes.Clear();

            if (ENABLE_LOG)
                DebugConsole.LogSuccess($"[ReactableSyncer][ON_PREFAB_INIT]{EntityName}:{NetId} initialized.");
        }

		public override void OnCleanUp()
		{
            if (ENABLE_LOG)
                DebugConsole.LogSuccess($"[ReactableSyncer][ON_CLEANUP]{EntityName}:{NetId} cleaning up.");

            reactableUpdateTimes.Clear();
            reactables.Clear();

			base.OnCleanUp();
		}

        public void RegisterReactable(Reactable reactable) {
            if (reactable == null)
            {
                DebugConsole.LogWarning($"[ReactableSyncer][REGISTER]{EntityName}:{NetId} register failed: null reactable.");
                return;
            }

            string name = reactable.GetType().Name;
            int nameHash = reactable.GetType().FullName.GetHashCode();

            reactables[nameHash] = (name, reactable);
            reactableUpdateTimes[nameHash] = Time.unscaledTime;

            if (ENABLE_LOG)
                DebugConsole.LogSuccess($"[ReactableSyncer][REGISTER]{EntityName}:{NetId} {name}:{nameHash} registed.");
        }

        public void RequestSyncReactable(Reactable reactable, int reactor_NetId, string reactor_name)
        {
            using var _ = Profiler.Scope();

            if (!isServer || !MultiplayerSession.SessionHasPlayers)
                return;

            if (reactable == null || reactor_NetId == 0)
                return;

            string reactableName = reactable.GetType().Name;
            int nameHash = reactable.GetType().FullName.GetHashCode();

            try
            {
                float time = Time.unscaledTime;

                CallClientRpc(nameof(RpcBeginReactable), time, nameHash, reactor_NetId);

                if (ENABLE_LOG)
                    DebugConsole.LogSuccess(GetLogStr("SEND_BEGIN", reactableName, nameHash, reactor_name, reactor_NetId, time, ""));
            }
            catch (Exception e)
            {
                DebugConsole.LogError(GetLogStr(
                    "SEND_BEGIN",
                    reactableName, nameHash,
                    reactor_name, reactor_NetId,
                    Time.unscaledTime, e.ToString()
                ));
            }
        }

        [ClientRpc(SendMode = (int)PacketSendMode.Reliable)]
		private void RpcBeginReactable(float timestamp, int nameHash, int reactor_NetId)
		{
			using var _ = Profiler.Scope();

            if (!isClient)
                return;

            if (!reactables.TryGetValue(nameHash, out var reactable))
            {
                DebugConsole.LogWarning(GetLogStr(
                    "RECEIVE_BEGIN",
                    "<unknown reactable>", nameHash,
                    "<unknown reactor>", reactor_NetId,
                    timestamp, "Failed to get reactable."
                ));

                return;
            }

            var (reactableName, reactableInstance) = reactable;

            if (!NetworkIdentityRegistry.TryGetComponent<NetworkIdentity>(reactor_NetId, out var identity))
            {
                DebugConsole.LogWarning(GetLogStr(
                    "RECEIVE_BEGIN",
                    reactableName, nameHash,
                    "<unknown reactor>", reactor_NetId,
                    timestamp, "Failed to get NetworkIdentity for reactor."
                ));

                return;
            }

            string reactorName = identity.gameObject.GetProperName();

            // The reactables store in the syncer just a cache.
            // They can be destroyed and then recreated by the game.
            if (reactableInstance == null)
            {
                DebugConsole.LogWarning(GetLogStr(
                    "RECEIVE_BEGIN",
                    reactableName, nameHash,
                    reactorName, reactor_NetId,
                    timestamp, "Reactable instance has been destroyed."
                ));

                return;
            }

            try
            {
                ReactionMonitor.Instance smi = identity.gameObject.GetSMI<ReactionMonitor.Instance>();
                if (smi == null)
                {
                    DebugConsole.LogWarning(GetLogStr(
                        "RECEIVE_BEGIN",
                        reactableName, nameHash,
                        reactorName, reactor_NetId,
                        timestamp, "Failed to get ReactionMonitor.Instance for reactor."
                    ));

                    return;
                }

                smi.sm.reactable.Set(reactableInstance, smi, false);
                smi.GoTo(smi.sm.reacting);

                if (ENABLE_LOG)
                    DebugConsole.LogSuccess(GetLogStr(
                        "RECEIVE_BEGIN",
                        reactableName, nameHash,
                        reactorName, reactor_NetId,
                        timestamp, $"Success to sync reactable. {smi.IsReacting()}"
                    ));
            }
            catch (Exception ex)
            {
                DebugConsole.LogError(GetLogStr(
                    "RECEIVE_BEGIN",
                    reactableName, nameHash,
                    reactorName, reactor_NetId,
                    timestamp, ex.ToString()
                ));
            }

		}

        public string GetLogStr(string tag, string name, int hash, string reactorName, int reactorNetId, float time, string message)
        {
            return $"[ReactableSyncer][{tag}][{EntityName}:{NetId}] {name}:{hash}, {reactorName}:{reactorNetId}, [{time}], {message}";
        }
	}
}
