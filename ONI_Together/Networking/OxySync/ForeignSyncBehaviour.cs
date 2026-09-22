using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ONI_Together.Networking.OxySync
{
    /// <summary>
    /// Reflection adapter that lets the ONI Together's OxySync loop drive a
    /// <c>Shared.OxySync.NetworkBehaviour</c> instance loaded from the
    /// <c>ONI_Together_API</c> assembly (a distinct runtime Type from the ONI Together's own copy).
    ///
    /// All members accessed here form the OxySync API; every lookup is defensive so a
    /// renamed/removed member degrades to a no-op rather than throwing.
    /// </summary>
    public sealed class ForeignSyncBehaviour : ISyncBehaviour
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly object _instance;
        private readonly Type _type;
        private readonly GameObject _gameObject;

        private readonly PropertyInfo _netIdProp;
        private readonly PropertyInfo _behaviourIdProp;
        private readonly PropertyInfo _interestGroupProp;
        private readonly FieldInfo _syncIntervalField;
        private readonly FieldInfo _lastSyncTimeField;
        private readonly FieldInfo _lastActiveSyncTimeField;

        private readonly PropertyInfo _syncVarFieldsProp;
        private readonly PropertyInfo _syncVarDirtyBitsProp;
        private readonly MethodInfo _getAndClearDirtyBits;
        private readonly MethodInfo _markAllDirty;
        private readonly MethodInfo _markSyncVarAsDirty;
        private readonly MethodInfo _syncLastSentValues;
        private readonly MethodInfo _applySyncVar;
        private readonly MethodInfo _invokeCommand;
        private readonly MethodInfo _invokeClientRpc;
        private readonly MethodInfo _invokeTargetRpc;

        private readonly FieldInfo _syncVarFieldInfo;
        private readonly FieldInfo _syncVarFieldHash;
        private readonly FieldInfo _syncVarFieldLastSent;
        private readonly FieldInfo _syncVarFieldEpsilon;
        private readonly FieldInfo _syncVarFieldInterestGroup;
        private readonly FieldInfo _syncVarFieldSendMode;

        private readonly PropertyInfo _commandsProp;
        private readonly PropertyInfo _clientRpcsProp;
        private readonly PropertyInfo _targetRpcsProp;
        private readonly FieldInfo _cachedMethodInfo;
        private readonly FieldInfo _cachedMethodHash;
        private readonly FieldInfo _cachedMethodArgTypes;
        private readonly FieldInfo _cachedMethodInterestGroup;

        private readonly List<SyncVarDescriptor> _syncVars = new();

        public ForeignSyncBehaviour(object instance)
        {
            _instance = instance;
            _type = instance.GetType();

            var component = instance as Component;
            _gameObject = component != null ? component.gameObject : null;

            _netIdProp = _type.GetProperty("NetId", InstanceFlags);
            _behaviourIdProp = _type.GetProperty("BehaviourId", InstanceFlags);
            _interestGroupProp = _type.GetProperty("InterestGroup", InstanceFlags);
            _syncIntervalField = _type.GetField("SyncInterval", InstanceFlags);
            _lastSyncTimeField = _type.GetField("_lastSyncTime", InstanceFlags);
            _lastActiveSyncTimeField = _type.GetField("_lastActiveSyncTime", InstanceFlags);

            _syncVarFieldsProp = _type.GetProperty("SyncVarFields", InstanceFlags);
            _syncVarDirtyBitsProp = _type.GetProperty("SyncVarDirtyBits", InstanceFlags);
            _getAndClearDirtyBits = _type.GetMethod("GetAndClearDirtyBits", InstanceFlags);
            _markAllDirty = _type.GetMethod("MarkAllDirty", InstanceFlags);
            _markSyncVarAsDirty = FindMethod("MarkSyncVarAsDirty", 1);
            _syncLastSentValues = _type.GetMethod("SyncLastSentValues", InstanceFlags);
            _applySyncVar = FindMethod("ApplySyncVar", 3);
            _invokeCommand = FindMethod("InvokeCommand", 2);
            _invokeClientRpc = FindMethod("InvokeClientRpc", 2);
            _invokeTargetRpc = FindMethod("InvokeTargetRpc", 2);

            _syncVarFieldInfo = null;
            _syncVarFieldHash = null;
            _syncVarFieldLastSent = null;
            _syncVarFieldEpsilon = null;
            _syncVarFieldInterestGroup = null;
            _syncVarFieldSendMode = null;

            if (_syncVarFieldsProp != null)
            {
                var elementType = GetSyncVarElementType(_syncVarFieldsProp.PropertyType);
                if (elementType != null)
                {
                    _syncVarFieldInfo = elementType.GetField("Info", InstanceFlags);
                    _syncVarFieldHash = elementType.GetField("Hash", InstanceFlags);
                    _syncVarFieldLastSent = elementType.GetField("LastSentValue", InstanceFlags);
                    _syncVarFieldEpsilon = elementType.GetField("Epsilon", InstanceFlags);
                    _syncVarFieldInterestGroup = elementType.GetField("InterestGroup", InstanceFlags);
                    _syncVarFieldSendMode = elementType.GetField("SendMode", InstanceFlags);
                }
            }

            _commandsProp = _type.GetProperty("Commands", InstanceFlags);
            _clientRpcsProp = _type.GetProperty("ClientRpcs", InstanceFlags);
            _targetRpcsProp = _type.GetProperty("TargetRpcs", InstanceFlags);

            // CachedMethod metadata is shared by every RPC collection.
            Type cachedMethodType = null;
            if (_commandsProp != null)
                cachedMethodType = GetDictionaryValueType(_commandsProp.PropertyType);
            if (cachedMethodType == null && _clientRpcsProp != null)
                cachedMethodType = GetDictionaryValueType(_clientRpcsProp.PropertyType);
            if (cachedMethodType == null && _targetRpcsProp != null)
                cachedMethodType = GetDictionaryValueType(_targetRpcsProp.PropertyType);

            if (cachedMethodType != null)
            {
                _cachedMethodInfo = cachedMethodType.GetField("Info", InstanceFlags);
                _cachedMethodHash = cachedMethodType.GetField("Hash", InstanceFlags);
                _cachedMethodArgTypes = cachedMethodType.GetField("ArgTypes", InstanceFlags);
                _cachedMethodInterestGroup = cachedMethodType.GetField("InterestGroup", InstanceFlags);
            }
        }

        public object Instance => _instance;

        private MethodInfo FindMethod(string name, int paramCount)
        {
            foreach (var m in _type.GetMethods(InstanceFlags))
            {
                if (m.Name == name && m.GetParameters().Length == paramCount)
                    return m;
            }
            return null;
        }

        private static Type GetSyncVarElementType(Type collectionType)
        {
            if (collectionType.IsArray)
                return collectionType.GetElementType();
            if (collectionType.IsGenericType)
            {
                var args = collectionType.GetGenericArguments();
                // IReadOnlyList<T> / IList<T> -> element; IReadOnlyDictionary<K,V> -> value
                return args.Length == 1 ? args[0] : args[1];
            }
            return null;
        }

        private static Type GetDictionaryValueType(Type collectionType)
        {
            if (collectionType.IsGenericType)
            {
                var args = collectionType.GetGenericArguments();
                if (args.Length == 2)
                    return args[1];
            }
            return null;
        }

        public int NetId
        {
            get => (int)(_netIdProp?.GetValue(_instance) ?? 0);
            set => _netIdProp?.SetValue(_instance, value);
        }

        public int BehaviourId
        {
            get => (int)(_behaviourIdProp?.GetValue(_instance) ?? 0);
            set => _behaviourIdProp?.SetValue(_instance, value);
        }

        public int InterestGroup
        {
            get => (int)(_interestGroupProp?.GetValue(_instance) ?? -1);
            set => _interestGroupProp?.SetValue(_instance, value);
        }

        public float SyncInterval => (float)(_syncIntervalField?.GetValue(_instance) ?? 0.5f);

        public float LastSyncTime
        {
            get => (float)(_lastSyncTimeField?.GetValue(_instance) ?? 0f);
            set => _lastSyncTimeField?.SetValue(_instance, value);
        }

        public float LastActiveSyncTime
        {
            get => (float)(_lastActiveSyncTimeField?.GetValue(_instance) ?? 0f);
            set => _lastActiveSyncTimeField?.SetValue(_instance, value);
        }

        public GameObject GameObject => _gameObject;
        public bool IsDestroyed => _gameObject == null || _gameObject.IsNullOrDestroyed();
        public Type UnderlyingType => _type;
        public System.Reflection.Assembly SourceAssembly => _type.Assembly;

        public int GetMyWorldId()
        {
            var method = _type.GetMethod("GetMyWorldId", InstanceFlags);
            if (method != null)
                return (int)(method.Invoke(_instance, null) ?? -1);
            return -1;
        }

        public int SyncVarCount => _syncVars.Count;

        public SyncVarDescriptor GetSyncVar(int index)
        {
            return index >= 0 && index < _syncVars.Count ? _syncVars[index] : default;
        }

        /// <summary>
        /// Rebuilds descriptors from the foreign behaviour's current SyncVarFields collection,
        /// preserving the FieldInfo binding needed for read/write.
        /// </summary>
        public void RefreshSyncVars()
        {
            if (_syncVarFieldsProp == null || _syncVarFieldInfo == null || _syncVarFieldHash == null)
                return;

            var collection = _syncVarFieldsProp.GetValue(_instance);
            if (collection == null)
                return;

            var list = collection as System.Collections.IList;
            int count = list?.Count ?? 0;

            _syncVars.Clear();

            for (int i = 0; i < count; i++)
            {
                var element = list[i];
                var fieldInfo = _syncVarFieldInfo.GetValue(element) as FieldInfo;
                if (fieldInfo == null)
                    continue;

                int hash = (int)(_syncVarFieldHash.GetValue(element) ?? 0);
                object lastSent = _syncVarFieldLastSent?.GetValue(element);
                float epsilon = (float)(_syncVarFieldEpsilon?.GetValue(element) ?? 0.01f);
                int interestGroup = (int)(_syncVarFieldInterestGroup?.GetValue(element) ?? -1);
                int sendMode = (int)(_syncVarFieldSendMode?.GetValue(element) ?? 0);

                _syncVars.Add(SyncVarDescriptor.FromField(
                    _instance, fieldInfo, hash, lastSent, epsilon, interestGroup, sendMode));
            }
        }

        public ulong GetAndClearDirtyBits()
            => (ulong)(_getAndClearDirtyBits?.Invoke(_instance, null) ?? 0UL);

        public ulong SyncVarDirtyBits
            => (ulong)(_syncVarDirtyBitsProp?.GetValue(_instance) ?? 0UL);

        public void MarkAllDirty() => _markAllDirty?.Invoke(_instance, null);

        public void MarkSyncVarDirty(int fieldHash)
            => _markSyncVarAsDirty?.Invoke(_instance, new object[] { fieldHash });

        public void SyncLastSentValues() => _syncLastSentValues?.Invoke(_instance, null);

        public void ApplySyncVar(int fieldHash, object value, long timestamp)
            => _applySyncVar?.Invoke(_instance, new object[] { fieldHash, value, timestamp });

        public void InvokeCommand(int methodHash, byte[] args)
            => _invokeCommand?.Invoke(_instance, new object[] { methodHash, args });

        public void InvokeClientRpc(int methodHash, byte[] args)
            => _invokeClientRpc?.Invoke(_instance, new object[] { methodHash, args });

        public void InvokeTargetRpc(int methodHash, byte[] args)
            => _invokeTargetRpc?.Invoke(_instance, new object[] { methodHash, args });

        public IReadOnlyList<RpcDescriptor> Commands => ConvertRpcCollection(_commandsProp);
        public IReadOnlyList<RpcDescriptor> ClientRpcs => ConvertRpcCollection(_clientRpcsProp);
        public IReadOnlyList<RpcDescriptor> TargetRpcs => ConvertRpcCollection(_targetRpcsProp);

        private IReadOnlyList<RpcDescriptor> ConvertRpcCollection(PropertyInfo collectionProp)
        {
            var empty = (IReadOnlyList<RpcDescriptor>)System.Array.Empty<RpcDescriptor>();
            if (collectionProp == null || _cachedMethodHash == null)
                return empty;

            var collection = collectionProp.GetValue(_instance) as System.Collections.IEnumerable;
            if (collection == null)
                return empty;

            var list = new List<RpcDescriptor>();
            foreach (var entry in collection)
            {
                // Each entry is a KeyValuePair<int, CachedMethod>; reflect its Value.
                var entryType = entry.GetType();
                var valueProp = entryType.GetProperty("Value", InstanceFlags);
                var cached = valueProp?.GetValue(entry);
                if (cached == null)
                    continue;

                var info = _cachedMethodInfo?.GetValue(cached) as MethodInfo;
                list.Add(new RpcDescriptor
                {
                    Name = info?.Name ?? "?",
                    Hash = (int)(_cachedMethodHash.GetValue(cached) ?? 0),
                    ArgTypes = _cachedMethodArgTypes?.GetValue(cached) as Type[] ?? System.Array.Empty<Type>(),
                    InterestGroup = (int)(_cachedMethodInterestGroup?.GetValue(cached) ?? -1),
                });
            }
            return list;
        }
    }
}
