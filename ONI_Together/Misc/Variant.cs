using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ONI_Together.Misc
{
    /// <summary>
    /// A type-safe tagged union that holds one value of multiple possible types (Float, Int, Byte, String, Boolean) at a time,
    /// identified by a discriminator (Type). Eliminates unsafe type punning and boxing by storing the actual typed value
    /// with self-describing serialization via BinaryWriter/BinaryReader.
    /// </summary>
    public struct Variant
    {
        public enum TypeCode : byte { Float, Int, Byte, String, Boolean, Vector3, Vector2 }

        public TypeCode Type;
        public float Float;
        public int Int;
        public byte Byte;
        public string String;
        public bool Boolean;
        public Vector3 Vector3;
        public Vector2 Vector2;

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
                _ => "Unknown"
            };
        }
    }
}
