using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Shared.OxySync
{
    public static class VariantHelper
    {
        private const int MAX_OBJECT_GRAPH_DEPTH = ClassSerializationPolicy.MaxObjectGraphDepth;

        public static Variant ObjectToVariant(object value)
            => ObjectToVariantInternal(value, new HashSet<object>(ClassSerializationPolicy.ObjectReferenceComparer), 0);

        private static Variant ObjectToVariantInternal(object value, HashSet<object> visitedRefs, int depth)
        {
            if (depth > MAX_OBJECT_GRAPH_DEPTH)
                throw new NotSupportedException($"OxySync Variant object graph exceeded max depth {MAX_OBJECT_GRAPH_DEPTH}.");

            if (value == null) return new Variant { Type = Variant.TypeCode.Null };
            if (value is UnityEngine.Object)
                throw new NotSupportedException(
                    $"Type '{value.GetType().FullName}' is a UnityEngine.Object and is not supported by OxySync Variant serialization.");
            if (value is int i) return i;
            if (value is float f) return f;
            if (value is byte b) return b;
            if (value is string s) return (Variant)s;
            if (value is bool bv) return bv;
            if (value is Vector3 v3) return v3;
            if (value is Vector2 v2) return v2;
            if (value is byte[] ba) return ba;
            if (value is Quaternion q) return q;
            if (value is HashedString hs) return hs;
            if (value is KAnimHashedString khs) return khs;
            if (value is short sh) return sh;
            if (value is ushort us) return us;
            if (value is uint ui) return ui;
            if (value is long l) return l;
            if (value is ulong ul) return ul;
            if (value is double d) return d;
            if (value is decimal dec) return dec;
            if (value is sbyte sb) return sb;
            if (value is char c) return c;
            if (value is Color col) return col;
            if (value is Enum e)
            {
                Type underlying = Enum.GetUnderlyingType(e.GetType());
                bool unsigned = underlying == typeof(byte) || underlying == typeof(ushort) ||
                    underlying == typeof(uint) || underlying == typeof(ulong);
                return unsigned ? (Variant)Convert.ToUInt64(e) : (Variant)Convert.ToInt64(e);
            }

            if (value is int[] iarr) return iarr;
            if (value is float[] farr) return farr;
            if (value is double[] darr) return darr;

            if (value is Array arr && value.GetType() != typeof(byte[]))
            {
                var variants = new Variant[arr.Length];
                for (int i2 = 0; i2 < arr.Length; i2++)
                    variants[i2] = ObjectToVariantInternal(arr.GetValue(i2), visitedRefs, depth + 1);
                return new Variant { Type = Variant.TypeCode.VariantArray, VariantArray = variants };
            }

            if (value is IDictionary dict)
            {
                var variants = new Variant[dict.Count * 2];
                int idx = 0;
                foreach (DictionaryEntry entry in dict)
                {
                    variants[idx++] = ObjectToVariantInternal(entry.Key, visitedRefs, depth + 1);
                    variants[idx++] = ObjectToVariantInternal(entry.Value, visitedRefs, depth + 1);
                }
                return new Variant { Type = Variant.TypeCode.VariantArray, VariantArray = variants };
            }

            var valueType = value.GetType();

            if (value is IEnumerable enumerable)
            {
                bool isStack = valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(Stack<>);

                var items = new List<object>();
                foreach (var item in enumerable)
                    items.Add(item);

                if (isStack)
                    items.Reverse();

                var variants = new Variant[items.Count];
                for (int i2 = 0; i2 < items.Count; i2++)
                    variants[i2] = ObjectToVariantInternal(items[i2], visitedRefs, depth + 1);
                return new Variant { Type = Variant.TypeCode.VariantArray, VariantArray = variants };
            }

            if (valueType.IsClass)
            {
                if (typeof(Delegate).IsAssignableFrom(valueType))
                    throw new NotSupportedException(
                        $"Type '{valueType.FullName}' is a delegate and is not supported by OxySync Variant serialization.");

                var rules = ClassSerializationPolicy.GetClassSerializationRules(valueType);
                if (!rules.IsEligible)
                    throw new NotSupportedException(
                        $"Type '{valueType.FullName}' is not eligible for OxySync class serialization." +
                        " Mark the class with [Serializable] or mark at least one field with [SerializeField].");

                if (!visitedRefs.Add(value))
                    throw new NotSupportedException(
                        $"Type '{valueType.FullName}' contains a circular reference and is not supported by OxySync Variant serialization.");

                try
                {
                    var fields = ClassSerializationPolicy.GetSerializableFields(valueType);
                    if (fields.Length == 0)
                        throw new NotSupportedException(
                            $"Type '{valueType.FullName}' has no serializable fields and is not supported by OxySync Variant serialization.");

                    var members = new Variant[fields.Length];
                    for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
                    {
                        FieldInfo field = fields[fieldIndex];
                        members[fieldIndex] = ObjectToVariantInternal(field.GetValue(value), visitedRefs, depth + 1);
                    }

                    return new Variant
                    {
                        Type = Variant.TypeCode.VariantArray,
                        VariantArray = members,
                    };
                }
                finally
                {
                    visitedRefs.Remove(value);
                }
            }

            throw new NotSupportedException(
                $"Type '{value?.GetType().FullName ?? "null"}' is not supported by OxySync Variant serialization.");
        }

        public static object VariantToObject(Variant v, Type targetType)
            => VariantToObjectInternal(v, targetType, 0);

        private static object VariantToObjectInternal(Variant v, Type targetType, int depth)
        {
            if (depth > MAX_OBJECT_GRAPH_DEPTH)
                throw new InvalidDataException($"OxySync Variant object graph exceeded max depth {MAX_OBJECT_GRAPH_DEPTH}.");

            if (v.Type == Variant.TypeCode.Null)
            {
                if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                    throw new InvalidDataException(
                        $"Null SyncVar cannot be assigned to non-nullable type '{targetType}'.");
                return null;
            }

            if (targetType == typeof(int)) return v.Int;
            if (targetType == typeof(float)) return v.Float;
            if (targetType == typeof(byte)) return v.Byte;
            if (targetType == typeof(string)) return v.String ?? string.Empty;
            if (targetType == typeof(bool)) return v.Boolean;
            if (targetType == typeof(Vector3)) return v.Vector3;
            if (targetType == typeof(Vector2)) return v.Vector2;
            if (targetType == typeof(byte[])) return v.ByteArray ?? Array.Empty<byte>();
            if (targetType == typeof(Quaternion)) return v.Quaternion;
            if (targetType == typeof(HashedString)) return new HashedString(v.Int);
            if (targetType == typeof(KAnimHashedString)) return new KAnimHashedString(v.Int);
            if (targetType == typeof(short)) return (short)v.Int;
            if (targetType == typeof(ushort)) return (ushort)v.Int;
            if (targetType == typeof(uint)) return (uint)v.Long;
            if (targetType == typeof(long)) return v.Long;
            if (targetType == typeof(ulong)) return v.ULong;
            if (targetType == typeof(double)) return v.Double;
            if (targetType == typeof(decimal)) return v.Decimal;
            if (targetType == typeof(sbyte)) return (sbyte)v.Byte;
            if (targetType == typeof(char)) return (char)v.Int;
            if (targetType == typeof(Color)) return v.Color;
            if (v.Type == Variant.TypeCode.VariantArray)
            {
                var variantArray = v.VariantArray ?? Array.Empty<Variant>();

                if (targetType.IsArray && targetType != typeof(byte[]))
                {
                    var elementType = targetType.GetElementType();
                    var arr = Array.CreateInstance(elementType, variantArray.Length);
                    for (int i = 0; i < variantArray.Length; i++)
                        arr.SetValue(VariantToObjectInternal(variantArray[i], elementType, depth + 1), i);
                    return arr;
                }

                if (targetType.IsGenericType)
                {
                    var def = targetType.GetGenericTypeDefinition();

                    if (def == typeof(List<>))
                    {
                        var elementType = targetType.GetGenericArguments()[0];
                        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
                        for (int i = 0; i < variantArray.Length; i++)
                            list.Add(VariantToObjectInternal(variantArray[i], elementType, depth + 1));
                        return list;
                    }

                    if (def == typeof(HashSet<>))
                    {
                        var elementType = targetType.GetGenericArguments()[0];
                        var hashSetType = typeof(HashSet<>).MakeGenericType(elementType);
                        var hashSet = Activator.CreateInstance(hashSetType);
                        var addMethod = hashSetType.GetMethod("Add");
                        for (int i = 0; i < variantArray.Length; i++)
                            addMethod.Invoke(hashSet, new[] { VariantToObjectInternal(variantArray[i], elementType, depth + 1) });
                        return hashSet;
                    }

                    if (def == typeof(Queue<>))
                    {
                        var elementType = targetType.GetGenericArguments()[0];
                        var queueType = typeof(Queue<>).MakeGenericType(elementType);
                        var queue = Activator.CreateInstance(queueType);
                        var enqueueMethod = queueType.GetMethod("Enqueue");
                        for (int i = 0; i < variantArray.Length; i++)
                            enqueueMethod.Invoke(queue, new[] { VariantToObjectInternal(variantArray[i], elementType, depth + 1) });
                        return queue;
                    }

                    if (def == typeof(Stack<>))
                    {
                        var elementType = targetType.GetGenericArguments()[0];
                        var stackType = typeof(Stack<>).MakeGenericType(elementType);
                        var stack = Activator.CreateInstance(stackType);
                        var pushMethod = stackType.GetMethod("Push");
                        for (int i = 0; i < variantArray.Length; i++)
                            pushMethod.Invoke(stack, new[] { VariantToObjectInternal(variantArray[i], elementType, depth + 1) });
                        return stack;
                    }

                    if (def == typeof(Dictionary<,>))
                    {
                        if ((variantArray.Length & 1) != 0)
                            throw new InvalidDataException("OxySync dictionary Variant contains an unmatched key/value entry.");
                        var keyType = targetType.GetGenericArguments()[0];
                        var valType = targetType.GetGenericArguments()[1];
                        var dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valType);
                        var dict = (IDictionary)Activator.CreateInstance(dictType);
                        for (int i = 0; i < variantArray.Length; i += 2)
                        {
                            var key = VariantToObjectInternal(variantArray[i], keyType, depth + 1);
                            var val = VariantToObjectInternal(variantArray[i + 1], valType, depth + 1);
                            dict.Add(key, val);
                        }
                        return dict;
                    }
                }

                if (targetType.IsClass && targetType != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(targetType))
                {
                    var rules = ClassSerializationPolicy.GetClassSerializationRules(targetType);
                    if (!rules.IsEligible)
                        throw new InvalidDataException(
                            $"Type '{targetType.FullName}' is not eligible for OxySync class deserialization." +
                            " Mark the class with [Serializable] or mark at least one field with [SerializeField].");

                    object instance;
                    try
                    {
                        instance = Activator.CreateInstance(targetType, true);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidDataException($"Unable to construct class '{targetType.FullName}' for OxySync Variant deserialization.", e);
                    }

                    var fields = ClassSerializationPolicy.GetSerializableFields(targetType);
                    if (variantArray.Length != fields.Length)
                    {
                        throw new InvalidDataException(
                            $"OxySync class payload field-count mismatch for '{targetType.FullName}'.");
                    }

                    for (int i = 0; i < fields.Length; i++)
                    {
                        FieldInfo field = fields[i];
                        Variant memberValue = variantArray[i];
                        object converted = VariantToObjectInternal(memberValue, field.FieldType, depth + 1);
                        field.SetValue(instance, converted);
                    }

                    return instance;
                }
            }

            if (targetType == typeof(int[])) return v.IntArray ?? Array.Empty<int>();
            if (targetType == typeof(float[])) return v.FloatArray ?? Array.Empty<float>();
            if (targetType == typeof(double[])) return v.DoubleArray ?? Array.Empty<double>();

            if (targetType.IsEnum)
            {
                object rawValue = v.Type switch
                {
                    Variant.TypeCode.ULong => v.ULong,
                    Variant.TypeCode.UInt => (uint)v.Long,
                    Variant.TypeCode.UShort => (ushort)v.Int,
                    Variant.TypeCode.Byte => v.Byte,
                    Variant.TypeCode.Long => v.Long,
                    Variant.TypeCode.Short => (short)v.Int,
                    Variant.TypeCode.SByte => (sbyte)v.Byte,
                    _ => v.Int,
                };
                return Enum.ToObject(targetType, rawValue);
            }
            return v.String ?? string.Empty;
        }

        public static bool ValuesDiffer(Variant a, Variant b, float epsilon)
        {
            if (a.Type != b.Type) return true;
            return a.Type switch
            {
                Variant.TypeCode.Float => Mathf.Abs(a.Float - b.Float) > epsilon,
                Variant.TypeCode.Int => a.Int != b.Int,
                Variant.TypeCode.Byte => a.Byte != b.Byte,
                Variant.TypeCode.String => a.String != b.String,
                Variant.TypeCode.Boolean => a.Boolean != b.Boolean,
                Variant.TypeCode.Vector3 => Vector3.Distance(a.Vector3, b.Vector3) > epsilon,
                Variant.TypeCode.Vector2 => Vector2.Distance(a.Vector2, b.Vector2) > epsilon,
                Variant.TypeCode.ByteArray => !ByteArraysEqual(a.ByteArray, b.ByteArray),
                Variant.TypeCode.Quaternion => Quaternion.Angle(a.Quaternion, b.Quaternion) > epsilon,
                Variant.TypeCode.HashedString => a.Int != b.Int,
                Variant.TypeCode.KAnimHashedString => a.Int != b.Int,
                Variant.TypeCode.Short => a.Int != b.Int,
                Variant.TypeCode.UShort => a.Int != b.Int,
                Variant.TypeCode.UInt => a.Long != b.Long,
                Variant.TypeCode.Long => a.Long != b.Long,
                Variant.TypeCode.ULong => a.ULong != b.ULong,
                Variant.TypeCode.Double => Math.Abs(a.Double - b.Double) > epsilon,
                Variant.TypeCode.Decimal => a.Decimal != b.Decimal,
                Variant.TypeCode.Null => false,
                Variant.TypeCode.SByte => a.Byte != b.Byte,
                Variant.TypeCode.Char => a.Int != b.Int,
                Variant.TypeCode.Color => Vector4.Distance(a.Color, b.Color) > epsilon,
                Variant.TypeCode.VariantArray => VariantArraysDiffer(a.VariantArray, b.VariantArray, epsilon),
                Variant.TypeCode.IntArray => !ArraysEqual(a.IntArray, b.IntArray),
                Variant.TypeCode.FloatArray => !ArraysEqual(a.FloatArray, b.FloatArray),
                Variant.TypeCode.DoubleArray => !ArraysEqual(a.DoubleArray, b.DoubleArray),
                _ => true,
            };
        }

        private static bool VariantArraysDiffer(Variant[]? a, Variant[]? b, float epsilon)
        {
            if (a == b) return false;
            if (a == null || b == null) return true;
            if (a.Length != b.Length) return true;
            for (int i = 0; i < a.Length; i++)
                if (ValuesDiffer(a[i], b[i], epsilon))
                    return true;
            return false;
        }

        private static bool ArraysEqual(int[]? a, int[]? b)
        {
            if (a == b) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static bool ArraysEqual(float[]? a, float[]? b)
        {
            if (a == b) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static bool ArraysEqual(double[]? a, double[]? b)
        {
            if (a == b) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static bool ByteArraysEqual(byte[]? a, byte[]? b)
        {
            if (a == b) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
