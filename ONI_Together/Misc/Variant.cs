using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ONI_Together.Misc
{
    /// <summary>
    /// A type-safe tagged union that holds one value of multiple possible types (Float, Int, Byte, String, Boolean, Vector3, Vector2) at a time,
    /// identified by a discriminator (Type). Eliminates unsafe type punning and boxing by storing the actual typed value
    /// with self-describing serialization via BinaryWriter/BinaryReader.
    /// </summary>
    public struct Variant
    {
        public enum TypeCode : byte { Float, Int, Byte, String, Boolean, Vector3, Vector2, ByteArray, Quaternion, HashedString, KAnimHashedString, Short, UShort, UInt, Long, Double, SByte, Char, Color }

        public TypeCode Type;
        public float Float;
        public int Int;
        public byte Byte;
        public string String;
        public bool Boolean;
        public Vector3 Vector3;
        public Vector2 Vector2;
        public byte[] ByteArray;
        public Quaternion Quaternion;
        public long Long;
        public double Double;
        public Color Color;

        public void Write(BinaryWriter writer)
        {
            writer.Write((byte)Type);
            switch (Type)
            {
                case TypeCode.Float: writer.Write(Float); break;
                case TypeCode.Int: writer.Write(Int); break;
                case TypeCode.Byte: writer.Write(Byte); break;
                case TypeCode.String: writer.Write(String); break;
                case TypeCode.Boolean: writer.Write(Boolean); break;
                case TypeCode.Vector3: writer.Write(Vector3); break;
                case TypeCode.Vector2: writer.Write(Vector2); break;
                case TypeCode.ByteArray: writer.Write(ByteArray.Length); writer.Write(ByteArray); break;
                case TypeCode.Quaternion: writer.Write(Quaternion.x); writer.Write(Quaternion.y); writer.Write(Quaternion.z); writer.Write(Quaternion.w); break;
                case TypeCode.HashedString: writer.Write(Int); break;
                case TypeCode.KAnimHashedString: writer.Write(Int); break;
                case TypeCode.Short: writer.Write((short)Int); break;
                case TypeCode.UShort: writer.Write((ushort)Int); break;
                case TypeCode.UInt: writer.Write((uint)Long); break;
                case TypeCode.Long: writer.Write(Long); break;
                case TypeCode.Double: writer.Write(Double); break;
                case TypeCode.SByte: writer.Write((sbyte)Byte); break;
                case TypeCode.Char: writer.Write((char)Int); break;
                case TypeCode.Color: writer.Write(Color.r); writer.Write(Color.g); writer.Write(Color.b); writer.Write(Color.a); break;
            }
        }

        public static Variant Read(BinaryReader reader)
        {
            var v = new Variant { Type = (TypeCode)reader.ReadByte() };
            switch (v.Type)
            {
                case TypeCode.Float: v.Float = reader.ReadSingle(); break;
                case TypeCode.Int: v.Int = reader.ReadInt32(); break;
                case TypeCode.Byte: v.Byte = reader.ReadByte(); break;
                case TypeCode.String: v.String = reader.ReadString(); break;
                case TypeCode.Boolean: v.Boolean = reader.ReadBoolean(); break;
                case TypeCode.Vector3: v.Vector3 = reader.ReadVector3(); break;
                case TypeCode.Vector2: v.Vector2 = reader.ReadVector2(); break;
                case TypeCode.ByteArray: v.ByteArray = reader.ReadBytes(reader.ReadInt32()); break;
                case TypeCode.Quaternion: v.Quaternion = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); break;
                case TypeCode.HashedString: v.Int = reader.ReadInt32(); break;
                case TypeCode.KAnimHashedString: v.Int = reader.ReadInt32(); break;
                case TypeCode.Short: v.Int = reader.ReadInt16(); break;
                case TypeCode.UShort: v.Int = reader.ReadUInt16(); break;
                case TypeCode.UInt: v.Long = reader.ReadUInt32(); break;
                case TypeCode.Long: v.Long = reader.ReadInt64(); break;
                case TypeCode.Double: v.Double = reader.ReadDouble(); break;
                case TypeCode.SByte: v.Byte = (byte)reader.ReadSByte(); break;
                case TypeCode.Char: v.Int = reader.ReadChar(); break;
                case TypeCode.Color: v.Color = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); break;
            }
            return v;
        }

        public static implicit operator Variant(float f) => new Variant { Type = TypeCode.Float, Float = f };
        public static implicit operator Variant(int i) => new Variant { Type = TypeCode.Int, Int = i };
        public static implicit operator Variant(byte b) => new Variant { Type = TypeCode.Byte, Byte = b };
        public static implicit operator Variant(string s) => new Variant { Type = TypeCode.String, String = s };
        public static implicit operator Variant(bool b) => new Variant { Type = TypeCode.Boolean, Boolean = b };
        public static implicit operator Variant(Vector3 v) => new Variant { Type = TypeCode.Vector3, Vector3 = v };
        public static implicit operator Variant(Vector2 v) => new Variant { Type = TypeCode.Vector2, Vector2 = v };
        public static implicit operator Variant(byte[] b) => new Variant { Type = TypeCode.ByteArray, ByteArray = b };
        public static implicit operator Variant(Quaternion q) => new Variant { Type = TypeCode.Quaternion, Quaternion = q };
        public static implicit operator Variant(short s) => new Variant { Type = TypeCode.Short, Int = s };
        public static implicit operator Variant(ushort us) => new Variant { Type = TypeCode.UShort, Int = us };
        public static implicit operator Variant(uint ui) => new Variant { Type = TypeCode.UInt, Long = ui };
        public static implicit operator Variant(long l) => new Variant { Type = TypeCode.Long, Long = l };
        public static implicit operator Variant(double d) => new Variant { Type = TypeCode.Double, Double = d };
        public static implicit operator Variant(sbyte sb) => new Variant { Type = TypeCode.SByte, Byte = (byte)sb };
        public static implicit operator Variant(char c) => new Variant { Type = TypeCode.Char, Int = c };
        public static implicit operator Variant(Color c) => new Variant { Type = TypeCode.Color, Color = c };
        public static implicit operator Variant(HashedString h) => new Variant { Type = TypeCode.HashedString, Int = h.hash };
        public static implicit operator Variant(KAnimHashedString h) => new Variant { Type = TypeCode.KAnimHashedString, Int = h.hash };

        public readonly override string ToString()
        {
            return Type switch
            {
                TypeCode.Float => Float.ToString("F4"),
                TypeCode.Int => Int.ToString(),
                TypeCode.Byte => Byte.ToString(),
                TypeCode.String => String,
                TypeCode.Boolean => Boolean.ToString(),
                TypeCode.Vector3 => Vector3.ToString(),
                TypeCode.Vector2 => Vector2.ToString(),
                TypeCode.ByteArray => $"byte[{ByteArray?.Length ?? 0}]",
                TypeCode.Quaternion => Quaternion.ToString(),
                TypeCode.HashedString => $"HashedString(0x{Int:X8})",
                TypeCode.KAnimHashedString => $"KAnimHashedString(0x{Int:X8})",
                TypeCode.Short => ((short)Int).ToString(),
                TypeCode.UShort => ((ushort)Int).ToString(),
                TypeCode.UInt => ((uint)Long).ToString(),
                TypeCode.Long => Long.ToString(),
                TypeCode.Double => Double.ToString("F4"),
                TypeCode.SByte => ((sbyte)Byte).ToString(),
                TypeCode.Char => ((char)Int).ToString(),
                TypeCode.Color => Color.ToString(),
                _ => "Unknown"
            };
        }
    }
}
