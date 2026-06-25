using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Shared.OxySync
{
    public static class RpcSerializer
    {
        private enum ArgType : byte
        {
            Int, Float, Bool, Byte, Long, Double, String,
            Vector2, Vector3, Color, Quaternion, ByteArray, ULong
        }

        private static readonly Dictionary<Type, ArgType> TypeToTag = new()
        {
            [typeof(int)] = ArgType.Int,
            [typeof(float)] = ArgType.Float,
            [typeof(bool)] = ArgType.Bool,
            [typeof(byte)] = ArgType.Byte,
            [typeof(long)] = ArgType.Long,
            [typeof(double)] = ArgType.Double,
            [typeof(string)] = ArgType.String,
            [typeof(Vector2)] = ArgType.Vector2,
            [typeof(Vector3)] = ArgType.Vector3,
            [typeof(Color)] = ArgType.Color,
            [typeof(Quaternion)] = ArgType.Quaternion,
            [typeof(byte[])] = ArgType.ByteArray,
            [typeof(ulong)] = ArgType.ULong,
        };

        public static bool IsSupportedType(Type t) => t.IsEnum || TypeToTag.ContainsKey(t);

        public static byte[] Serialize(object[] args, Type[] argTypes)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            for (int i = 0; i < args.Length; i++)
            {
                WriteArg(writer, args[i], argTypes[i]);
            }

            return ms.ToArray();
        }

        public static object[] Deserialize(byte[] data, Type[] argTypes)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            var result = new object[argTypes.Length];
            for (int i = 0; i < argTypes.Length; i++)
            {
                result[i] = ReadArg(reader, argTypes[i]);
            }

            return result;
        }

        private static void WriteArg(BinaryWriter writer, object value, Type type)
        {
            if (type.IsEnum)
            {
                writer.Write((byte)ArgType.Int);
                writer.Write(Convert.ToInt32(value));
                return;
            }

            ArgType tag = TypeToTag[type];
            writer.Write((byte)tag);

            switch (tag)
            {
                case ArgType.Int: writer.Write((int)value); break;
                case ArgType.Float: writer.Write((float)value); break;
                case ArgType.Bool: writer.Write((bool)value); break;
                case ArgType.Byte: writer.Write((byte)value); break;
                case ArgType.Long:      writer.Write((long)value); break;
                case ArgType.ULong:     writer.Write((ulong)value); break;
                case ArgType.Double:    writer.Write((double)value); break;
                case ArgType.String: writer.Write((string)value ?? string.Empty); break;
                case ArgType.Vector2: writer.Write((Vector2)value); break;
                case ArgType.Vector3: writer.Write((Vector3)value); break;
                case ArgType.Color:
                    var c = (Color)value;
                    writer.Write(c.r);
                    writer.Write(c.g);
                    writer.Write(c.b);
                    writer.Write(c.a);
                    break;
                case ArgType.Quaternion:
                    var q = (Quaternion)value;
                    writer.Write(q.x);
                    writer.Write(q.y);
                    writer.Write(q.z);
                    writer.Write(q.w);
                    break;
                case ArgType.ByteArray:
                    var ba = (byte[])value;
                    writer.Write(ba.Length);
                    writer.Write(ba);
                    break;
            }
        }

        private static object ReadArg(BinaryReader reader, Type type)
        {
            ArgType tag = (ArgType)reader.ReadByte();

            switch (tag)
            {
                case ArgType.Int:
                    int intVal = reader.ReadInt32();
                    return type.IsEnum ? Enum.ToObject(type, intVal) : intVal;
                case ArgType.Float: return reader.ReadSingle();
                case ArgType.Bool: return reader.ReadBoolean();
                case ArgType.Byte: return reader.ReadByte();
                case ArgType.Long: return reader.ReadInt64();
                case ArgType.ULong: return reader.ReadUInt64();
                case ArgType.Double: return reader.ReadDouble();
                case ArgType.String: return reader.ReadString();
                case ArgType.Vector2: return reader.ReadVector2();
                case ArgType.Vector3: return reader.ReadVector3();
                case ArgType.Color:
                    return new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                case ArgType.Quaternion:
                    return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                case ArgType.ByteArray:
                    return reader.ReadBytes(reader.ReadInt32());
                default:
                    throw new InvalidDataException($"Unknown RPC arg type tag: {tag}");
            }
        }
    }
}
