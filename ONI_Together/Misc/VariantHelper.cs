using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ONI_Together.Misc
{
    public static class VariantHelper
    {
        public static Variant ObjectToVariant(object? value)
        {
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
            if (value is double d) return d;
            if (value is sbyte sb) return sb;
            if (value is char c) return c;
            if (value is Color col) return col;
            if (value is Enum e) return Convert.ToInt32(e);

            if (value is int[] iarr) return iarr;
            if (value is float[] farr) return farr;
            if (value is double[] darr) return darr;

            if (value is Array arr && value.GetType() != typeof(byte[]))
            {
                var variants = new Variant[arr.Length];
                for (int i2 = 0; i2 < arr.Length; i2++)
                    variants[i2] = ObjectToVariant(arr.GetValue(i2));
                return new Variant { Type = Variant.TypeCode.VariantArray, VariantArray = variants };
            }

            if (value is IDictionary dict)
            {
                var variants = new Variant[dict.Count * 2];
                int idx = 0;
                foreach (DictionaryEntry entry in dict)
                {
                    variants[idx++] = ObjectToVariant(entry.Key);
                    variants[idx++] = ObjectToVariant(entry.Value);
                }
                return new Variant { Type = Variant.TypeCode.VariantArray, VariantArray = variants };
            }

            if (value is IEnumerable enumerable)
            {
                var type = value.GetType();
                bool isStack = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Stack<>);

                var items = new List<object>();
                foreach (var item in enumerable)
                    items.Add(item);

                if (isStack)
                    items.Reverse();

                var variants = new Variant[items.Count];
                for (int i2 = 0; i2 < items.Count; i2++)
                    variants[i2] = ObjectToVariant(items[i2]);
                return new Variant { Type = Variant.TypeCode.VariantArray, VariantArray = variants };
            }

            return 0;
        }

        public static object VariantToObject(Variant v, Type targetType)
        {
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
            if (targetType == typeof(double)) return v.Double;
            if (targetType == typeof(sbyte)) return (sbyte)v.Byte;
            if (targetType == typeof(char)) return (char)v.Int;
            if (targetType == typeof(Color)) return v.Color;
            if (targetType == typeof(int[])) return v.IntArray ?? Array.Empty<int>();
            if (targetType == typeof(float[])) return v.FloatArray ?? Array.Empty<float>();
            if (targetType == typeof(double[])) return v.DoubleArray ?? Array.Empty<double>();
            if (v.Type == Variant.TypeCode.VariantArray)
            {
                if (targetType.IsArray && targetType != typeof(byte[]))
                {
                    var elementType = targetType.GetElementType();
                    var arr = Array.CreateInstance(elementType, v.VariantArray.Length);
                    for (int i = 0; i < v.VariantArray.Length; i++)
                        arr.SetValue(VariantToObject(v.VariantArray[i], elementType), i);
                    return arr;
                }

                if (targetType.IsGenericType)
                {
                    var def = targetType.GetGenericTypeDefinition();

                    if (def == typeof(List<>))
                    {
                        var elementType = targetType.GetGenericArguments()[0];
                        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
                        for (int i = 0; i < v.VariantArray.Length; i++)
                            list.Add(VariantToObject(v.VariantArray[i], elementType));
                        return list;
                    }

                    if (def == typeof(HashSet<>))
                    {
                        var elementType = targetType.GetGenericArguments()[0];
                        var hashSetType = typeof(HashSet<>).MakeGenericType(elementType);
                        var hashSet = Activator.CreateInstance(hashSetType);
                        var addMethod = hashSetType.GetMethod("Add");
                        for (int i = 0; i < v.VariantArray.Length; i++)
                            addMethod.Invoke(hashSet, new[] { VariantToObject(v.VariantArray[i], elementType) });
                        return hashSet;
                    }

                    if (def == typeof(Queue<>))
                    {
                        var elementType = targetType.GetGenericArguments()[0];
                        var queueType = typeof(Queue<>).MakeGenericType(elementType);
                        var queue = Activator.CreateInstance(queueType);
                        var enqueueMethod = queueType.GetMethod("Enqueue");
                        for (int i = 0; i < v.VariantArray.Length; i++)
                            enqueueMethod.Invoke(queue, new[] { VariantToObject(v.VariantArray[i], elementType) });
                        return queue;
                    }

                    if (def == typeof(Stack<>))
                    {
                        var elementType = targetType.GetGenericArguments()[0];
                        var stackType = typeof(Stack<>).MakeGenericType(elementType);
                        var stack = Activator.CreateInstance(stackType);
                        var pushMethod = stackType.GetMethod("Push");
                        for (int i = 0; i < v.VariantArray.Length; i++)
                            pushMethod.Invoke(stack, new[] { VariantToObject(v.VariantArray[i], elementType) });
                        return stack;
                    }

                    if (def == typeof(Dictionary<,>))
                    {
                        var keyType = targetType.GetGenericArguments()[0];
                        var valType = targetType.GetGenericArguments()[1];
                        var dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valType);
                        var dict = (IDictionary)Activator.CreateInstance(dictType);
                        for (int i = 0; i < v.VariantArray.Length; i += 2)
                        {
                            var key = VariantToObject(v.VariantArray[i], keyType);
                            var val = VariantToObject(v.VariantArray[i + 1], valType);
                            dict.Add(key, val);
                        }
                        return dict;
                    }
                }
            }

            if (targetType.IsEnum) return Enum.ToObject(targetType, v.Int);
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
                Variant.TypeCode.Double => Math.Abs(a.Double - b.Double) > epsilon,
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
