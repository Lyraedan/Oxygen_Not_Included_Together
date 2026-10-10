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
    [SkipSaveFileSerialization]
    [FixedInterestGroup]
	public class ReactableSyncer : NetworkBehaviour
	{
        // Logging flag for debugging
        private static readonly bool ENABLE_LOG = false;

        private readonly Dictionary<int, Reactable> reactables = new();
        private readonly HashSet<(int, int)> authorized = new();

        private string EntityName => gameObject?.GetProperName() ?? "Unknown Entity";

        public static bool networkReactionReplayStatic = false;

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

            if (ENABLE_LOG)
                DebugConsole.LogSuccess($"[ReactableSyncer][ON_PREFAB_INIT]{EntityName}:{NetId} initialized.");
        }

		public override void OnCleanUp()
		{
            if (ENABLE_LOG)
                DebugConsole.LogSuccess($"[ReactableSyncer][ON_CLEANUP]{EntityName}:{NetId} cleaning up.");

            reactables.Clear();

			base.OnCleanUp();
		}

        public void RegisterReactable(Reactable reactable) {
            if (reactable == null)
            {
                DebugConsole.LogWarning($"[ReactableSyncer][REGISTER]{EntityName}:{NetId} register failed: null reactable.");
                return;
            }

            int id = reactable.id.hash;
            string name = reactable.GetType().Name;

            if (reactables.TryGetValue(id, out var existing) && existing != null && existing.GetType() != reactable.GetType())
            {
                DebugConsole.LogWarning(
                    $"[ReactableSyncer][REGISTER]{EntityName}:{NetId} " +
                    $"reactable id={id} is used by a different type. " +
                    $"contact to the developer to change the design ASAP.");
            }

            reactables[id] = reactable;

            if (ENABLE_LOG)
                DebugConsole.Log(
                    $"[ReactableSyncer][REGISTER]{EntityName}:{NetId} " +
                    $"{name}:{id} added to reactables.");
        }

        public void RequestSyncAuthorization(Reactable reactable, GameObject reactor)
        {
            using var _ = Profiler.Scope();

            if (!isServer || !MultiplayerSession.SessionHasPlayers)
                return;

            if (!CanAuthorize(reactable, reactor, out var reactor_NetId, out var reactorName))
                return;

            int id = reactable.id.hash;
            string name = reactable.GetType().Name;
            float time = Time.unscaledTime;

            try
            {
                CallClientRpc(nameof(RpcAuthorizeReactable), time, reactable.id.hash, reactor_NetId);

                if (ENABLE_LOG)
                    DebugConsole.LogSuccess(GetLogStr(
                        "SEND_AUTH", name, id,
                        reactorName, reactor_NetId,
                        time, ""));
            }
            catch (Exception e)
            {
                DebugConsole.LogError(GetLogStr(
                    "SEND_AUTH", name, id,
                    reactorName, reactor_NetId,
                    time, e.ToString()
                ));
            }
        }

        [ClientRpc(SendMode = (int)PacketSendMode.ReliableImmediate)]
        private void RpcAuthorizeReactable(float time, int reactableId, int reactor_NetId)
        {
            bool successAuthorized = authorized.Add((reactableId, reactor_NetId));

            if (ENABLE_LOG)
            {
                var reactable = reactables.GetValueOrDefault(reactableId, null);
                string reactableName = reactable != null ? reactable.GetType().Name : "<unknown>";

                NetworkIdentityRegistry.TryGet(reactor_NetId, out var reactorIdentity);
                string reactorName = reactorIdentity != null && reactorIdentity.NetId != 0
                    ? reactorIdentity.gameObject.GetType().Name
                    : "<unknown reactor>";

                DebugConsole.LogSuccess(GetLogStr(
                    "RECEIVE_AUTH", reactableName, reactableId,
                    reactorName, reactor_NetId,
                    time, $"Recording authorization: {successAuthorized}"));
            }
        }

        private bool CanAuthorize(Reactable reactable, GameObject reactor, out int reactor_NetId, out string reactorName)
        {
            reactor_NetId = 0;
            reactorName = "<unknown reactor>";

            // Get into this case for some reason. must be something wrong in the codebase.
            if (NetId == 0)
            {
                DebugConsole.LogWarning($"[ReactableSyncer][CAN_AUTH]{EntityName}:{NetId} request failed: NetId is 0.");
                return false;
            }

            if (reactable == null)
                return false;

            int id = reactable.id.hash;
            string name = reactable.GetType().Name;
            float time = Time.unscaledTime;

            if (reactor == null)
            {
                DebugConsole.LogWarning(GetLogStr(
                        "CAN_AUTH", name, id,
                        reactorName, reactor_NetId,
                        time, "Reactor is null."));
                return false;
            }

            reactorName = reactor.GetType().Name;

            if (!reactor.TryGetComponent<NetworkIdentity>(out var identity) || identity.NetId == 0)
            {
                DebugConsole.LogWarning(GetLogStr(
                        "CAN_AUTH", name, id,
                        reactorName, reactor_NetId,
                        time, "reactor has no valid NetworkIdentity.")); 
                return false;
            }

            reactor_NetId = identity.NetId;

            return true;
        }

        public bool IsAuthorized(Reactable reactable, GameObject reactor)
        {
            if (!CanAuthorize(reactable, reactor, out var reactor_NetId, out var reactorName))
                return false;

            if (!authorized.Contains((reactable.id.hash, reactor_NetId)))
                return false;

            authorized.Remove((reactable.id.hash, reactor_NetId));

            if (ENABLE_LOG)
                DebugConsole.LogSuccess(GetLogStr(
                    "APPLY_AUTH", reactable.GetType().Name, reactable.id.hash,
                    reactorName, reactor_NetId,
                    Time.time, $"Successfully authorized"));

            return true;
        }

        public string GetLogStr(string tag, string name, int hash, string reactorName, int reactorNetId, float time, string message)
        {
            return $"[ReactableSyncer][{tag}][{EntityName}:{NetId}] {name}:{hash}, {reactorName}:{reactorNetId}, [{time}], {message}";
        }
	}
}
