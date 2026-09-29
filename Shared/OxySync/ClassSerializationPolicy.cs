using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Shared.OxySync
{
    public static class ClassSerializationPolicy
    {
        private const BindingFlags MEMBER_FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public const int MaxObjectGraphDepth = 32;

        public readonly struct Rules
        {
            public readonly bool IsEligible;
            public readonly bool IncludePublicFields;

            public Rules(bool isEligible, bool includePublicFields)
            {
                IsEligible = isEligible;
                IncludePublicFields = includePublicFields;
            }
        }

        private static readonly Dictionary<Type, Rules> ClassSerializationRulesCache = new Dictionary<Type, Rules>();
        private static readonly Dictionary<Type, FieldInfo[]> SerializableFieldCache = new Dictionary<Type, FieldInfo[]>();

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        public static IEqualityComparer<object> ObjectReferenceComparer => ReferenceEqualityComparer.Instance;

        public static FieldInfo[] GetSerializableFields(Type type)
        {
            if (SerializableFieldCache.TryGetValue(type, out FieldInfo[] cached))
                return cached;

            Rules rules = GetClassSerializationRules(type);
            if (!rules.IsEligible)
            {
                SerializableFieldCache[type] = Array.Empty<FieldInfo>();
                return SerializableFieldCache[type];
            }

            var list = new List<FieldInfo>();
            var type_ = type;
            while (type_ != null)
            {
                var fields = type_.GetFields(MEMBER_FLAGS | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (field.IsStatic || field.IsInitOnly || field.IsLiteral)
                        continue;
                    if (field.IsDefined(typeof(NonSerializedAttribute), true))
                        continue;
                    if (typeof(Delegate).IsAssignableFrom(field.FieldType))
                        continue;

                    bool hasSerializeField = field.IsDefined(typeof(SerializeField), true);
                    bool isIncludedPublic = rules.IncludePublicFields && field.IsPublic;
                    if (!hasSerializeField && !isIncludedPublic)
                        continue;

                    list.Add(field);
                }

                type_ = type_.BaseType;
            }

            list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            var result = list.ToArray();
            SerializableFieldCache[type] = result;
            return result;
        }

        public static Rules GetClassSerializationRules(Type type)
        {
            if (ClassSerializationRulesCache.TryGetValue(type, out Rules cached))
                return cached;

            Rules rules;
            if (type.IsDefined(typeof(SerializableAttribute), true))
            {
                rules = new Rules(isEligible: true, includePublicFields: true);
            }
            else if (HasExplicitSerializeField(type))
            {
                rules = new Rules(isEligible: true, includePublicFields: false);
            }
            else
            {
                rules = new Rules(isEligible: false, includePublicFields: false);
            }

            ClassSerializationRulesCache[type] = rules;
            return rules;
        }

        private static bool HasExplicitSerializeField(Type type)
        {
            var type_ = type;
            while (type_ != null)
            {
                var fields = type_.GetFields(MEMBER_FLAGS | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (field.IsStatic || field.IsLiteral)
                        continue;
                    if (field.IsDefined(typeof(SerializeField), true))
                        return true;
                }

                type_ = type_.BaseType;
            }

            return false;
        }
    }
}
