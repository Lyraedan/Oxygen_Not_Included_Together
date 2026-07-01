using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Shared.OxySync.Attributes;

namespace Shared.OxySync
{
    public abstract class NetworkBehaviour : KMonoBehaviour
    {
        private const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public static event Action<NetworkBehaviour>? OnSpawned;
        public static event Action<NetworkBehaviour>? OnBehaviourCleanUp;

        public static Func<bool>? IsHostQuery;
        public static Func<bool>? IsClientQuery;
        public static Func<bool>? InSessionQuery;

        public static Func<int, int, byte[], bool>? SendCommandToHost;
        public static Func<int, int, byte[], bool>? SendClientRpcToAll;
        public static Func<int, int, int, byte[], bool>? SendClientRpcToGroup;
        public static Func<ulong, int, int, byte[], bool>? SendTargetRpcToPlayer;

        public static Func<NetworkBehaviour, int>? NetIdQuery;
        public static Action<NetworkBehaviour, int>? NetIdSetter;
        public static Action<string>? LogWarning;
        public static Func<ulong>? LocalUserIdQuery;

        private List<SyncVarField>? _syncVarFields;
        private Dictionary<int, CachedMethod>? _commandMethods;
        private Dictionary<int, CachedMethod>? _clientRpcMethods;
        private Dictionary<int, CachedMethod>? _targetRpcMethods;
        private uint _syncVarDirtyBits;
        private Dictionary<int, int>? _syncVarHashToIndex;

        protected bool isServer => IsHostQuery?.Invoke() ?? false;
        protected bool isClient => IsClientQuery?.Invoke() ?? false;
        protected bool inSession => InSessionQuery?.Invoke() ?? false;

        public virtual int NetId
        {
            get => NetIdQuery?.Invoke(this) ?? 0;
            set => NetIdSetter?.Invoke(this, value);
        }

        public float SyncInterval = 0.5f;
        public float _lastSyncTime;
        public float _lastActiveSyncTime;
        public int InterestGroup { get; set; } = -1;

        public IReadOnlyList<SyncVarField> SyncVarFields =>
            _syncVarFields ?? (IReadOnlyList<SyncVarField>)Array.Empty<SyncVarField>();

        public IReadOnlyDictionary<int, CachedMethod> Commands =>
            _commandMethods ?? (IReadOnlyDictionary<int, CachedMethod>)new Dictionary<int, CachedMethod>();

        public IReadOnlyDictionary<int, CachedMethod> ClientRpcs =>
            _clientRpcMethods ?? (IReadOnlyDictionary<int, CachedMethod>)new Dictionary<int, CachedMethod>();

        public IReadOnlyDictionary<int, CachedMethod> TargetRpcs =>
            _targetRpcMethods ?? (IReadOnlyDictionary<int, CachedMethod>)new Dictionary<int, CachedMethod>();

        public struct SyncVarField
        {
            public FieldInfo Info;
            public int Hash;
            public object? LastSentValue;
            public MethodInfo? Hook;
            public float Epsilon;
            public int InterestGroup;
        }

        public struct CachedMethod
        {
            public MethodInfo Info;
            public int Hash;
            public Type[] ArgTypes;
            public int InterestGroup;
        }

        public override void OnSpawn()
        {
            base.OnSpawn();
            DiscoverSyncVars();
            DiscoverRpcs();
            OnSpawned?.Invoke(this);
        }

        public override void OnCleanUp()
        {
            OnBehaviourCleanUp?.Invoke(this);
            base.OnCleanUp();
        }

        private void DiscoverSyncVars()
        {
            var fields = GetType().GetFields(FLAGS);
            var list = new List<SyncVarField>();
            int classDefaultGroup = GetType().GetCustomAttribute<InterestGroupAttribute>()?.Group ?? -1;

            foreach (var field in fields)
            {
                var attr = field.GetCustomAttribute<SyncVarAttribute>();
                if (attr == null) continue;

                MethodInfo? hook = null;
                if (!string.IsNullOrEmpty(attr.Hook))
                {
                    hook = GetType().GetMethod(attr.Hook, FLAGS);
                }

                int group = attr.InterestGroup != -1 ? attr.InterestGroup : classDefaultGroup;

                list.Add(new SyncVarField
                {
                    Info = field,
                    Hash = field.Name.GetHashCode(),
                    LastSentValue = field.GetValue(this),
                    Hook = hook,
                    Epsilon = attr.Epsilon,
                    InterestGroup = group,
                });
            }

            _syncVarFields = list;

            _syncVarHashToIndex = new Dictionary<int, int>();
            for (int i = 0; i < list.Count; i++)
                _syncVarHashToIndex[list[i].Hash] = i;

            _syncVarDirtyBits = 0;
        }

        private void DiscoverRpcs()
        {
            var methods = GetType().GetMethods(FLAGS);
            int classDefaultGroup = GetType().GetCustomAttribute<InterestGroupAttribute>()?.Group ?? -1;

            foreach (var method in methods)
            {
                if (method.GetCustomAttribute<CommandAttribute>() != null)
                {
                    (_commandMethods ??= new())[method.Name.GetHashCode()] = MakeCachedMethod(method);
                }
                if (method.GetCustomAttribute<ClientRpcAttribute>() != null)
                {
                    (_clientRpcMethods ??= new())[method.Name.GetHashCode()] = MakeCachedMethod(method, method.GetCustomAttribute<ClientRpcAttribute>()!.InterestGroup, classDefaultGroup);
                }
                if (method.GetCustomAttribute<TargetRpcAttribute>() != null)
                {
                    (_targetRpcMethods ??= new())[method.Name.GetHashCode()] = MakeCachedMethod(method);
                }
            }
        }

        private static CachedMethod MakeCachedMethod(MethodInfo method)
        {
            return new CachedMethod
            {
                Info = method,
                Hash = method.Name.GetHashCode(),
                ArgTypes = method.GetParameters().Select(p => p.ParameterType).ToArray(),
                InterestGroup = -1,
            };
        }

        private static CachedMethod MakeCachedMethod(MethodInfo method, int attrGroup, int classDefaultGroup)
        {
            return new CachedMethod
            {
                Info = method,
                Hash = method.Name.GetHashCode(),
                ArgTypes = method.GetParameters().Select(p => p.ParameterType).ToArray(),
                InterestGroup = attrGroup != -1 ? attrGroup : classDefaultGroup,
            };
        }

        protected void CallCommand(string methodName, params object[] args)
        {
            if (!inSession) return;

            var hash = methodName.GetHashCode();
            var argTypes = GetCommandArgTypes(hash);
            var serialized = RpcSerializer.Serialize(args, argTypes);

            if (isServer)
            {
                InvokeCommand(hash, serialized);
                return;
            }

            SendCommandToHost?.Invoke(NetId, hash, serialized);
        }

        protected void CallClientRpc(string methodName, params object[] args)
        {
            if (!inSession || !isServer) return;

            var hash = methodName.GetHashCode();
            var argTypes = GetClientRpcArgTypes(hash);
            var serialized = RpcSerializer.Serialize(args, argTypes);

            int group = GetClientRpcGroup(hash);
            if (group == -1) group = InterestGroup;
            if (group == -1)
                SendClientRpcToAll?.Invoke(NetId, hash, serialized);
            else
                SendClientRpcToGroup?.Invoke(group, NetId, hash, serialized);
        }

        protected void CallClientRpc(int interestGroup, string methodName, params object[] args)
        {
            if (!inSession || !isServer) return;

            var hash = methodName.GetHashCode();
            var argTypes = GetClientRpcArgTypes(hash);
            var serialized = RpcSerializer.Serialize(args, argTypes);

            if (interestGroup == -1)
                SendClientRpcToAll?.Invoke(NetId, hash, serialized);
            else
                SendClientRpcToGroup?.Invoke(interestGroup, NetId, hash, serialized);
        }

        protected void CallTargetRpc(ulong targetPlayer, string methodName, params object[] args)
        {
            if (!inSession || !isServer) return;

            var hash = methodName.GetHashCode();
            var argTypes = GetTargetRpcArgTypes(hash);
            var serialized = RpcSerializer.Serialize(args, argTypes);

            SendTargetRpcToPlayer?.Invoke(targetPlayer, NetId, hash, serialized);
        }

        public virtual void ApplySyncVar(int fieldHash, object value, long timestamp = 0)
        {
            if (_syncVarFields == null) return;

            for (int i = 0; i < _syncVarFields.Count; i++)
            {
                var field = _syncVarFields[i];
                if (field.Hash != fieldHash) continue;

                var oldValue = field.Info.GetValue(this);
                field.Info.SetValue(this, value);

                var updated = field;
                updated.LastSentValue = value;
                _syncVarFields[i] = updated;

                if (field.Hook != null && !Equals(oldValue, value))
                {
                    field.Hook.Invoke(this, new[] { oldValue, value });
                }
                return;
            }
        }

        public void InvokeCommand(int methodHash, byte[] args)
        {
            if (_commandMethods == null) return;
            if (!_commandMethods.TryGetValue(methodHash, out var method)) return;
            InvokeMethod(method, args);
        }

        public void InvokeClientRpc(int methodHash, byte[] args)
        {
            if (_clientRpcMethods == null) return;
            if (!_clientRpcMethods.TryGetValue(methodHash, out var method)) return;
            InvokeMethod(method, args);
        }

        public void InvokeTargetRpc(int methodHash, byte[] args)
        {
            if (_targetRpcMethods == null) return;
            if (!_targetRpcMethods.TryGetValue(methodHash, out var method)) return;
            InvokeMethod(method, args);
        }

        private void InvokeMethod(CachedMethod method, byte[] args)
        {
            if (method.Info.GetCustomAttribute<ServerAttribute>() != null && !isServer)
            {
                LogWarning?.Invoke($"[OxySync] '{method.Info.Name}' is [Server] but called on client — skipped.");
                return;
            }
            if (method.Info.GetCustomAttribute<ClientAttribute>() != null && !isClient)
            {
                LogWarning?.Invoke($"[OxySync] '{method.Info.Name}' is [Client] but called on server — skipped.");
                return;
            }
            if (method.Info.GetCustomAttribute<CommandAttribute>()?.RequiresHost == true && !isServer)
            {
                LogWarning?.Invoke($"[OxySync] Command '{method.Info.Name}' requires host — skipped.");
                return;
            }

            if (method.ArgTypes.Length == 0)
            {
                method.Info.Invoke(this, null);
                return;
            }

            var deserialized = RpcSerializer.Deserialize(args, method.ArgTypes);
            method.Info.Invoke(this, deserialized);
        }

        private Type[] GetCommandArgTypes(int hash)
        {
            if (_commandMethods != null && _commandMethods.TryGetValue(hash, out var m))
                return m.ArgTypes;
            return Array.Empty<Type>();
        }

        private Type[] GetClientRpcArgTypes(int hash)
        {
            if (_clientRpcMethods != null && _clientRpcMethods.TryGetValue(hash, out var m))
                return m.ArgTypes;
            return Array.Empty<Type>();
        }

        private int GetClientRpcGroup(int hash)
        {
            if (_clientRpcMethods != null && _clientRpcMethods.TryGetValue(hash, out var m))
                return m.InterestGroup;
            return -1;
        }

        private Type[] GetTargetRpcArgTypes(int hash)
        {
            if (_targetRpcMethods != null && _targetRpcMethods.TryGetValue(hash, out var m))
                return m.ArgTypes;
            return Array.Empty<Type>();
        }

        public object? GetSyncVarValue(int fieldHash)
        {
            if (_syncVarFields == null) return null;
            foreach (var field in _syncVarFields)
            {
                if (field.Hash == fieldHash)
                    return field.Info.GetValue(this);
            }
            return null;
        }

        public void SetSyncVarValue(int fieldHash, object value)
        {
            if (_syncVarFields == null) return;
            for (int i = 0; i < _syncVarFields.Count; i++)
            {
                var field = _syncVarFields[i];
                if (field.Hash != fieldHash) continue;

                field.Info.SetValue(this, value);
                var updated = field;
                updated.LastSentValue = value;
                _syncVarFields[i] = updated;
                SetSyncVarDirty(fieldHash);
                return;
            }
        }

        public void SyncLastSentValues()
        {
            if (_syncVarFields == null) return;
            for (int i = 0; i < _syncVarFields.Count; i++)
            {
                var field = _syncVarFields[i];
                field.LastSentValue = field.Info.GetValue(this);
                _syncVarFields[i] = field;
            }
        }

        protected void SetSyncVarDirty(int fieldHash)
        {
            if (_syncVarHashToIndex != null && _syncVarHashToIndex.TryGetValue(fieldHash, out int idx))
                _syncVarDirtyBits |= 1u << idx;
        }

        public uint GetAndClearDirtyBits()
        {
            uint bits = _syncVarDirtyBits;
            _syncVarDirtyBits = 0;
            return bits;
        }

        public void MarkAllDirty()
        {
            if (_syncVarFields != null)
                _syncVarDirtyBits = _syncVarFields.Count < 32 ? (1u << _syncVarFields.Count) - 1 : ~0u;
        }
    }
}
