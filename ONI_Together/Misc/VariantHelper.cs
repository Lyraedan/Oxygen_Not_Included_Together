using System;
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
            if (value is Enum e) return Convert.ToInt32(e);
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
                _ => true,
            };
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
