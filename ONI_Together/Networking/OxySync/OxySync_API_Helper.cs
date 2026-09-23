using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.Networking.OxySync.Packets;
using Shared.Profiling;
using UnityEngine;
using Expr = System.Linq.Expressions.Expression;

namespace ONI_Together.Networking.OxySync
{
    /// <summary>
    /// Runtime bridge that lets the ONI Together's OxySync system drive
    /// <c>Shared.OxySync.NetworkBehaviour</c> instances that live in an <c>ONI_Together_API</c>
    /// assembly.
    /// </summary>
    public static class OxySync_API_Helper
    {
        private const string ApiAssemblyName = "ONI_Together_API";
        private const string NetworkBehaviourTypeName = "Shared.OxySync.NetworkBehaviour";
        private const string NetIdentityHelperTypeName = "Shared.Helpers.NetIdentityHelper";

        private static bool _assemblyLoadSubscribed;

        private static readonly List<BridgedApiAssembly> _bridgedAssemblies = new();
        private static readonly HashSet<Assembly> _seenAssemblies = new();

        private static readonly Dictionary<(int NetId, int BehaviourId), ForeignSyncBehaviour> _foreignLookup = new();

        /// <summary>True if at least one API-consuming mod was found and bridged.</summary>
        public static bool IsBridged => _bridgedAssemblies.Count > 0;

        /// <summary>Number of distinct API assemblies bridged (one per consuming mod).</summary>
        public static int BridgedAssemblyCount => _bridgedAssemblies.Count;

        /// <summary>
        /// Per-assembly bridge state: the foreign OxySync types and the delegate instances we
        /// keep rooted for the lifetime of the mod (the static event handlers in particular).
        /// </summary>
        private sealed class BridgedApiAssembly
        {
            public Assembly Assembly;
            public Type NetworkBehaviourType;
            public Type NetIdentityHelperType;

            public string ConsumingModName;

            public Delegate OnSpawnedHandler;
            public Delegate OnCleanUpHandler;
        }

        /// <summary>
        /// Scans loaded assemblies (and future loads) for API copies and wires each one into the
        /// main mod. Safe to call repeatedly.
        /// </summary>
        public static void Initialize()
        {
            using var _ = Profiler.Scope();

            SubscribeAssemblyLoad();

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    TryBridgeAssembly(asm);
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[OxySync-API] Failed to scan assemblies for OxySync bridge: {ex}");
            }
        }

        private static void SubscribeAssemblyLoad()
        {
            if (_assemblyLoadSubscribed)
                return;

            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            _assemblyLoadSubscribed = true;
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            TryBridgeAssembly(args.LoadedAssembly);
        }

        private static void TryBridgeAssembly(Assembly asm)
        {
            if (asm == null)
                return;

            if (_seenAssemblies.Contains(asm))
                return;

            if (asm.GetName().Name != ApiAssemblyName)
                return;

            try
            {
                var networkBehaviourType = asm.GetType(NetworkBehaviourTypeName, throwOnError: false);
                if (networkBehaviourType == null)
                    return;

                var bridge = new BridgedApiAssembly
                {
                    Assembly = asm,
                    NetworkBehaviourType = networkBehaviourType,
                    NetIdentityHelperType = asm.GetType(NetIdentityHelperTypeName, throwOnError: false),
                    ConsumingModName = ResolveConsumingModName(asm),
                };

                HookStaticEvents(bridge);
                WireStaticDelegates(bridge);

                // Only mark as seen once bridged so a failure can be retried on a later load/re-scan.
                _seenAssemblies.Add(asm);
                _bridgedAssemblies.Add(bridge);
                DebugConsole.LogSuccess($"[OxySync-API] Bridged OxySync surface from API copy: {asm.FullName}");
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[OxySync-API] Failed to bridge API copy {asm.FullName}: {ex}");
            }
        }
        
        private static string ResolveConsumingModName(Assembly apiAssembly)
        {
            try
            {
                var apiName = apiAssembly.GetName().Name;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm == apiAssembly) continue;

                    var name = asm.GetName().Name;
                    if (name == ApiAssemblyName || name == "ONI_Together" || name == "Shared") // Skip self
                        continue;

                    foreach (var reference in asm.GetReferencedAssemblies())
                    {
                        if (reference.Name == apiName)
                            return name;
                    }
                }
            }
            catch
            {
            }

            return apiAssembly.GetName().Name;
        }
        
        public static string GetSourceName(Assembly assembly)
        {
            if (assembly == null)
                return "?";

            for (int i = 0; i < _bridgedAssemblies.Count; i++)
            {
                if (_bridgedAssemblies[i].Assembly == assembly)
                    return _bridgedAssemblies[i].ConsumingModName;
            }

            return assembly.GetName().Name;
        }

        private static void HookStaticEvents(BridgedApiAssembly bridge)
        {
            var onSpawnedEvent = bridge.NetworkBehaviourType.GetEvent(
                "OnSpawned", BindingFlags.Public | BindingFlags.Static);
            var onCleanUpEvent = bridge.NetworkBehaviourType.GetEvent(
                "OnBehaviourCleanUp", BindingFlags.Public | BindingFlags.Static);

            if (onSpawnedEvent != null)
            {
                bridge.OnSpawnedHandler = BuildInstanceHandler(onSpawnedEvent.EventHandlerType, nameof(HandleForeignSpawned));
                onSpawnedEvent.AddEventHandler(null, bridge.OnSpawnedHandler);
            }

            if (onCleanUpEvent != null)
            {
                bridge.OnCleanUpHandler = BuildInstanceHandler(onCleanUpEvent.EventHandlerType, nameof(HandleForeignCleanUp));
                onCleanUpEvent.AddEventHandler(null, bridge.OnCleanUpHandler);
            }
        }

        /// <summary>
        /// Builds an <c>Action&lt;ForeignNetworkBehaviour&gt;</c> that dispatches the instance (boxed)
        /// into the named handler on this helper.
        /// </summary>
        private static Delegate BuildInstanceHandler(Type delegateType, string handlerName)
        {
            var invoke = delegateType.GetMethod("Invoke");
            var parameters = invoke.GetParameters();
            var arg = Expr.Parameter(parameters[0].ParameterType, "instance");

            var handler = typeof(OxySync_API_Helper).GetMethod(handlerName, BindingFlags.Static | BindingFlags.NonPublic);
            var body = Expr.Call(handler, Expr.Convert(arg, typeof(object)));

            return Expr.Lambda(delegateType, body, arg).Compile();
        }

        private static void HandleForeignSpawned(object instance)
        {
            if (instance == null || OxySyncManager.Instance == null) return;

            try
            {
                var adapter = new ForeignSyncBehaviour(instance);
                if (adapter.NetId == 0)
                    return;

                _foreignLookup[(adapter.NetId, adapter.BehaviourId)] = adapter;
                OxySyncManager.Instance.RegisterSyncBehaviour(adapter);
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[OxySync-API] Register foreign behaviour failed: {ex}");
            }
        }

        private static void HandleForeignCleanUp(object instance)
        {
            if (instance == null) return;

            try
            {
                var keysToRemove = new List<(int, int)>();
                foreach (var kvp in _foreignLookup)
                {
                    if (kvp.Value.Instance != instance) continue;
                    OxySyncManager.Instance?.UnregisterSyncBehaviour(kvp.Value);
                    keysToRemove.Add(kvp.Key);
                }

                foreach (var key in keysToRemove)
                    _foreignLookup.Remove(key);
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[OxySync-API] Unregister foreign behaviour failed: {ex}");
            }
        }

        private static void WireStaticDelegates(BridgedApiAssembly bridge)
        {
            TryWire(() => SetStaticDelegate(bridge, "IsHostQuery", (Func<bool>)(() => MultiplayerSession.IsHost)));
            TryWire(() => SetStaticDelegate(bridge, "IsClientQuery", (Func<bool>)(() => MultiplayerSession.IsClient)));
            TryWire(() => SetStaticDelegate(bridge, "InSessionQuery", (Func<bool>)(() => MultiplayerSession.InActiveSession)));
            TryWire(() => SetStaticDelegate(bridge, "LocalUserIdQuery", (Func<ulong>)(() => MultiplayerSession.LocalUserID)));
            TryWire(() => SetStaticDelegate(bridge, "LogWarning", (Action<string>)(msg => DebugConsole.LogWarning(msg))));

            TryWire(() => SetStaticDelegate(bridge, "NetIdQuery", BuildNetIdQuery(bridge)));
            TryWire(() => SetStaticDelegate(bridge, "NetIdSetter", BuildNetIdSetter(bridge)));
            
            TryWire(() => SetStaticDelegate(bridge, "SendCommandToHost",
                BuildRpcDelegate(typeof(Func<int, int, int, byte[], int, bool>), nameof(SendCommandToHostImpl))));
            TryWire(() => SetStaticDelegate(bridge, "SendClientRpcToAll",
                BuildRpcDelegate(typeof(Func<int, int, int, byte[], int, bool>), nameof(SendClientRpcToAllImpl))));
            TryWire(() => SetStaticDelegate(bridge, "SendClientRpcToGroup",
                BuildRpcDelegate(typeof(Func<int, int, int, int, byte[], int, bool>), nameof(SendClientRpcToGroupImpl))));
            TryWire(() => SetStaticDelegate(bridge, "SendTargetRpcToPlayer",
                BuildRpcDelegate(typeof(Func<ulong, int, int, int, byte[], int, bool>), nameof(SendTargetRpcToPlayerImpl))));

            TryWire(() => WireNetIdentityHelper(bridge));
        }

        private static void TryWire(System.Action wire)
        {
            try
            {
                wire();
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[OxySync-API] Failed to wire an OxySync delegate: {ex.Message}");
            }
        }

        private static void WireNetIdentityHelper(BridgedApiAssembly bridge)
        {
            if (bridge.NetIdentityHelperType == null)
                return;

            SetStaticDelegateOnType(bridge.NetIdentityHelperType, "SetIdentity",
                (Func<GameObject, int, int>)((go, netId) => OxySyncManager.SetOrGetIdentity(go, netId)));
            SetStaticDelegateOnType(bridge.NetIdentityHelperType, "AddIdentity",
                (Func<GameObject, int>)(go => OxySyncManager.AddIdentity(go)));
            SetStaticDelegateOnType(bridge.NetIdentityHelperType, "GetIdentity",
                (Func<GameObject, int>)(go => OxySyncManager.GetIdentity(go)));
            SetStaticDelegateOnType(bridge.NetIdentityHelperType, "OverrideIdentity",
                (Func<GameObject, int, int>)((go, netId) => OxySyncManager.OverrideIdentity(go, netId)));
        }

        private static void SetStaticDelegate(BridgedApiAssembly bridge, string fieldName, Delegate value)
        {
            SetStaticDelegateOnType(bridge.NetworkBehaviourType, fieldName, value);
        }

        private static void SetStaticDelegateOnType(Type type, string fieldName, Delegate value)
        {
            var field = type?.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            field?.SetValue(null, value);
        }

        // Delegate builders

        private static Delegate BuildNetIdQuery(BridgedApiAssembly bridge)
        {
            var funcType = typeof(Func<,>).MakeGenericType(bridge.NetworkBehaviourType, typeof(int));
            var param = Expr.Parameter(bridge.NetworkBehaviourType, "b");

            var impl = typeof(OxySync_API_Helper).GetMethod(
                nameof(NetIdQueryImpl), BindingFlags.Static | BindingFlags.NonPublic);
            var body = Expr.Call(impl, Expr.Convert(param, typeof(object)));

            return Expr.Lambda(funcType, body, param).Compile();
        }

        private static int NetIdQueryImpl(object behaviour)
        {
            var component = behaviour as Component;
            if (component == null)
                return 0;
            return component.GetComponent<NetworkIdentity>()?.NetId ?? 0;
        }

        private static Delegate BuildNetIdSetter(BridgedApiAssembly bridge)
        {
            var actionType = typeof(Action<,>).MakeGenericType(bridge.NetworkBehaviourType, typeof(int));
            var param = Expr.Parameter(bridge.NetworkBehaviourType, "b");
            var value = Expr.Parameter(typeof(int), "v");

            var impl = typeof(OxySync_API_Helper).GetMethod(
                nameof(NetIdSetterImpl), BindingFlags.Static | BindingFlags.NonPublic);
            var body = Expr.Call(impl, Expr.Convert(param, typeof(object)), value);

            return Expr.Lambda(actionType, body, param, value).Compile();
        }

        private static void NetIdSetterImpl(object behaviour, int netId)
        {
            var component = behaviour as Component;
            if (component == null)
                return;
            component.gameObject.AddOrGet<NetworkIdentity>().OverrideNetId(netId);
        }
        
        private static Delegate BuildRpcDelegate(Type funcType, string implName)
        {
            var invoke = funcType.GetMethod("Invoke");
            var parameters = invoke.GetParameters();

            var argExpressions = new ParameterExpression[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
                argExpressions[i] = Expr.Parameter(parameters[i].ParameterType, $"a{i}");

            var impl = typeof(OxySync_API_Helper).GetMethod(implName, BindingFlags.Static | BindingFlags.NonPublic);
            var body = Expr.Call(impl, argExpressions);
            return Expr.Lambda(funcType, body, argExpressions).Compile();
        }

        private static bool SendCommandToHostImpl(int netId, int behaviourId, int hash, byte[] args, int sendType)
        {
            PacketSender.SendToHost(new CommandPacket
            {
                NetId = netId,
                BehaviourId = behaviourId,
                MethodHash = hash,
                Args = args,
            }, (PacketSendMode)sendType);
            return true;
        }

        private static bool SendClientRpcToAllImpl(int netId, int behaviourId, int hash, byte[] args, int sendType)
        {
            PacketSender.SendToAllClients(new ClientRpcPacket
            {
                NetId = netId,
                BehaviourId = behaviourId,
                MethodHash = hash,
                Args = args,
                TargetPlayerId = ulong.MaxValue,
            }, (PacketSendMode)sendType);
            return true;
        }

        private static bool SendClientRpcToGroupImpl(int group, int netId, int behaviourId, int hash, byte[] args, int sendType)
        {
            PacketSender.SendToGroup(group, new ClientRpcPacket
            {
                NetId = netId,
                BehaviourId = behaviourId,
                MethodHash = hash,
                Args = args,
                TargetPlayerId = ulong.MaxValue,
            }, (PacketSendMode)sendType);
            return true;
        }

        private static bool SendTargetRpcToPlayerImpl(ulong target, int netId, int behaviourId, int hash, byte[] args, int sendType)
        {
            PacketSender.SendToPlayer(target, new ClientRpcPacket
            {
                NetId = netId,
                BehaviourId = behaviourId,
                MethodHash = hash,
                Args = args,
                TargetPlayerId = target,
            }, (PacketSendMode)sendType);
            return true;
        }

        /// <summary>
        /// Looks up an API-assembly behaviour registered on an entity by NetId/BehaviourId.
        /// Used as a fallback when the behaviour isn't in the manager registry.
        /// </summary>
        public static bool TryGetForeignBehaviour(int netId, int behaviourId, out ISyncBehaviour behaviour)
        {
            behaviour = null;
            if (_bridgedAssemblies.Count == 0)
                return false;

            if (_foreignLookup.TryGetValue((netId, behaviourId), out var adapter))
            {
                behaviour = adapter;
                return true;
            }

            if (!NetworkIdentityRegistry.TryGet(netId, out var identity) || identity == null || identity.gameObject.IsNullOrDestroyed())
                return false;

            for (int i = 0; i < _bridgedAssemblies.Count; i++)
            {
                var component = identity.gameObject.GetComponent(_bridgedAssemblies[i].NetworkBehaviourType);
                if (component == null)
                    continue;

                adapter = new ForeignSyncBehaviour(component);
                _foreignLookup[(netId, behaviourId)] = adapter;
                behaviour = adapter;
                return true;
            }

            return false;
        }
    }
}
