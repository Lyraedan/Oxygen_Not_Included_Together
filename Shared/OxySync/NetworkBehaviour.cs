using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Linq.Expressions;
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

        public static Func<int, int, byte[], int, bool>? SendCommandToHost;
        public static Func<int, int, byte[], int, bool>? SendClientRpcToAll;
        public static Func<int, int, int, byte[], int, bool>? SendClientRpcToGroup;
        public static Func<ulong, int, int, byte[], int, bool>? SendTargetRpcToPlayer;

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
            public int SendMode;
        }

        public struct CachedMethod
        {
            public MethodInfo Info;
            public int Hash;
            public Type[] ArgTypes;
            public int InterestGroup;
            public int SendMode;
            public bool IncludeHost;
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

        private static IEnumerable<FieldInfo> GetFieldsIncludingBaseTypes(Type type)
        {
            var type_ = type;
            while (type_ != null)
            {
                foreach (var field in type_.GetFields(FLAGS | BindingFlags.DeclaredOnly))
                    yield return field;

                type_ = type_.BaseType;
            }
        }

        private void DiscoverSyncVars()
        {
            var fields = GetFieldsIncludingBaseTypes(GetType());
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
                    SendMode = attr.SendMode,
                });
            }

            _syncVarFields = list;

            _syncVarHashToIndex = new Dictionary<int, int>();
            for (int i = 0; i < list.Count; i++)
                _syncVarHashToIndex[list[i].Hash] = i;

            _syncVarDirtyBits = 0;
        }

        private static IEnumerable<MethodInfo> GetMethodsIncludingBaseTypes(Type type)
        {
            var type_ = type;
            while (type_ != null)
            {
                foreach (var method in type_.GetMethods(FLAGS | BindingFlags.DeclaredOnly))
                    yield return method;

                type_ = type_.BaseType;
            }
        }

        private void DiscoverRpcs()
        {
            var methods = GetMethodsIncludingBaseTypes(GetType());
            int classDefaultGroup = GetType().GetCustomAttribute<InterestGroupAttribute>()?.Group ?? -1;

            foreach (var method in methods)
            {
                var cmdAttr = method.GetCustomAttribute<CommandAttribute>();
                if (cmdAttr != null)
                {
                    ValidateMethodPrefix(method, "Cmd", "Command");
                    (_commandMethods ??= new())[method.Name.GetHashCode()] = MakeCachedMethod(method, cmdAttr);
                }
                var rpcAttr = method.GetCustomAttribute<ClientRpcAttribute>();
                if (rpcAttr != null)
                {
                    ValidateMethodPrefix(method, "Rpc", "ClientRpc");
                    (_clientRpcMethods ??= new())[method.Name.GetHashCode()] = MakeCachedMethod(method, rpcAttr, classDefaultGroup);
                }
                var tgtAttr = method.GetCustomAttribute<TargetRpcAttribute>();
                if (tgtAttr != null)
                {
                    ValidateMethodPrefix(method, "Target", "TargetRpc");
                    (_targetRpcMethods ??= new())[method.Name.GetHashCode()] = MakeCachedMethod(method, tgtAttr);
                }
            }
        }

        private static void ValidateMethodPrefix(MethodInfo method, string expectedPrefix, string attributeName)
        {
            if (!method.Name.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                LogWarning?.Invoke(
                    $"[OxySync] Method '{method.Name}' has [{attributeName}] but name doesn't start with '{expectedPrefix}'. " +
                    $"Rename to '{expectedPrefix}{method.Name}' to follow convention."
                );
            }
        }

        private static CachedMethod MakeCachedMethod(MethodInfo method, CommandAttribute attr)
        {
            return new CachedMethod
            {
                Info = method,
                Hash = method.Name.GetHashCode(),
                ArgTypes = method.GetParameters().Select(p => p.ParameterType).ToArray(),
                InterestGroup = -1,
                SendMode = attr.SendMode,
            };
        }

        private static CachedMethod MakeCachedMethod(MethodInfo method, ClientRpcAttribute attr, int classDefaultGroup)
        {
            return new CachedMethod
            {
                Info = method,
                Hash = method.Name.GetHashCode(),
                ArgTypes = method.GetParameters().Select(p => p.ParameterType).ToArray(),
                InterestGroup = attr.InterestGroup != -1 ? attr.InterestGroup : classDefaultGroup,
                SendMode = attr.SendMode,
                IncludeHost = attr.IncludeHost,
            };
        }

        private static CachedMethod MakeCachedMethod(MethodInfo method, TargetRpcAttribute attr)
        {
            return new CachedMethod
            {
                Info = method,
                Hash = method.Name.GetHashCode(),
                ArgTypes = method.GetParameters().Select(p => p.ParameterType).ToArray(),
                InterestGroup = -1,
                SendMode = attr.SendMode,
            };
        }

        protected void CallCommand(string methodName, params object[] args)
        {
            if (!inSession) return;

            var hash = methodName.GetHashCode();

            if (_commandMethods == null || !_commandMethods.ContainsKey(hash))
            {
                LogWarning?.Invoke($"[OxySync] '{methodName}' is not a registered Command on {GetType().Name}.");
                return;
            }

            var argTypes = GetCommandArgTypes(hash);
            var serialized = RpcSerializer.Serialize(args, argTypes);

            if (isServer)
            {
                InvokeCommand(hash, serialized);
                return;
            }

            var sendMode = GetCommandSendMode(hash);
            SendCommandToHost?.Invoke(NetId, hash, serialized, sendMode);
        }

        protected void CallCommand(Expression<Action> expr)
        {
            if (expr.Body is MethodCallExpression mce)
                CallCommand(mce.Method.Name, ExtractArgs(expr));
        }

        protected void CallCommand(Delegate method, params object[] args)
        {
            CallCommand(method.Method.Name, args);
        }

        private static object?[] ExtractArgs(Expression<Action> expr)
        {
            if (expr.Body is not MethodCallExpression mce)
                throw new ArgumentException("Expression must be a method call.");
            return mce.Arguments.Select(a => EvaluateExpression(a)).ToArray();
        }

        private static object? EvaluateExpression(System.Linq.Expressions.Expression expression)
        {
            if (expression is ConstantExpression constant)
                return constant.Value;
            return System.Linq.Expressions.Expression.Lambda<Func<object?>>(
                System.Linq.Expressions.Expression.Convert(expression, typeof(object))
            ).Compile()();
        }

        protected void CallClientRpc(string methodName, params object[] args)
        {
            if (!inSession || !isServer) return;

            var hash = methodName.GetHashCode();

            if (_clientRpcMethods == null || !_clientRpcMethods.ContainsKey(hash))
            {
                LogWarning?.Invoke($"[OxySync] '{methodName}' is not a registered ClientRpc on {GetType().Name}.");
                return;
            }

            var argTypes = GetClientRpcArgTypes(hash);
            var serialized = RpcSerializer.Serialize(args, argTypes);

            int group = GetClientRpcGroup(hash);
            if (group == -1) group = InterestGroup;
            var sendMode = GetClientRpcSendMode(hash);
            if (group == -1)
                SendClientRpcToAll?.Invoke(NetId, hash, serialized, sendMode);
            else
                SendClientRpcToGroup?.Invoke(group, NetId, hash, serialized, sendMode);

            if (GetClientRpcIncludeHost(hash))
                InvokeClientRpc(hash, serialized);
        }

        protected void CallClientRpc(Expression<Action> expr)
        {
            if (expr.Body is MethodCallExpression mce)
                CallClientRpc(mce.Method.Name, ExtractArgs(expr));
        }

        protected void CallClientRpc(Delegate method, params object[] args)
        {
            CallClientRpc(method.Method.Name, args);
        }

        protected void CallClientRpc(int interestGroup, string methodName, params object[] args)
        {
            if (!inSession || !isServer) return;

            var hash = methodName.GetHashCode();

            if (_clientRpcMethods == null || !_clientRpcMethods.ContainsKey(hash))
            {
                LogWarning?.Invoke($"[OxySync] '{methodName}' is not a registered ClientRpc on {GetType().Name}.");
                return;
            }

            var argTypes = GetClientRpcArgTypes(hash);
            var serialized = RpcSerializer.Serialize(args, argTypes);

            var sendMode = GetClientRpcSendMode(hash);
            if (interestGroup == -1)
                SendClientRpcToAll?.Invoke(NetId, hash, serialized, sendMode);
            else
                SendClientRpcToGroup?.Invoke(interestGroup, NetId, hash, serialized, sendMode);

            if (GetClientRpcIncludeHost(hash))
                InvokeClientRpc(hash, serialized);
        }

        protected void CallClientRpc(int interestGroup, Expression<Action> expr)
        {
            if (expr.Body is MethodCallExpression mce)
                CallClientRpc(interestGroup, mce.Method.Name, ExtractArgs(expr));
        }

        protected void CallClientRpc(int interestGroup, Delegate method, params object[] args)
        {
            CallClientRpc(interestGroup, method.Method.Name, args);
        }

        protected void CallTargetRpc(ulong targetPlayer, string methodName, params object[] args)
        {
            if (!inSession || !isServer) return;

            var hash = methodName.GetHashCode();

            if (_targetRpcMethods == null || !_targetRpcMethods.ContainsKey(hash))
            {
                LogWarning?.Invoke($"[OxySync] '{methodName}' is not a registered TargetRpc on {GetType().Name}.");
                return;
            }

            var argTypes = GetTargetRpcArgTypes(hash);
            var serialized = RpcSerializer.Serialize(args, argTypes);

            var sendMode = GetTargetRpcSendMode(hash);
            SendTargetRpcToPlayer?.Invoke(targetPlayer, NetId, hash, serialized, sendMode);
        }

        protected void CallTargetRpc(ulong targetPlayer, Expression<Action> expr)
        {
            if (expr.Body is MethodCallExpression mce)
                CallTargetRpc(targetPlayer, mce.Method.Name, ExtractArgs(expr));
        }

        protected void CallTargetRpc(ulong targetPlayer, Delegate method, params object[] args)
        {
            CallTargetRpc(targetPlayer, method.Method.Name, args);
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

        private bool GetClientRpcIncludeHost(int hash)
        {
            if (_clientRpcMethods != null && _clientRpcMethods.TryGetValue(hash, out var m))
                return m.IncludeHost;
            return false;
        }

        private int GetCommandSendMode(int hash)
        {
            if (_commandMethods != null && _commandMethods.TryGetValue(hash, out var m))
                return m.SendMode;
            return 9; // PacketSendMode.ReliableImmediate
        }

        private int GetClientRpcSendMode(int hash)
        {
            if (_clientRpcMethods != null && _clientRpcMethods.TryGetValue(hash, out var m))
                return m.SendMode;
            return 8; // PacketSendMode.Reliable
        }

        private int GetTargetRpcSendMode(int hash)
        {
            if (_targetRpcMethods != null && _targetRpcMethods.TryGetValue(hash, out var m))
                return m.SendMode;
            return 9; // PacketSendMode.ReliableImmediate
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
