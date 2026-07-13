using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Shared.OxySync
{
    public static class RpcSerializer
    {
        private enum ArgType : byte
        {
            Int, Float, Bool, Byte, Long, Double, String,
            Vector2, Vector3, Color, Quaternion, ByteArray, ULong,
            Array, List, Dict, Short, UShort, UInt, SByte, Char, Decimal,
            Nullable, HashSet, Queue, Stack
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
            [typeof(short)] = ArgType.Short,
            [typeof(ushort)] = ArgType.UShort,
            [typeof(uint)] = ArgType.UInt,
            [typeof(sbyte)] = ArgType.SByte,
            [typeof(char)] = ArgType.Char,
            [typeof(decimal)] = ArgType.Decimal,
        };

        public static bool IsSupportedType(Type t)
        {
            if (t.IsEnum) return true;
            if (t.IsSubclassOf(typeof(Delegate)) || t.IsSubclassOf(typeof(MulticastDelegate))) return false;
            if (TypeToTag.ContainsKey(t)) return true;

            if (t.IsArray)
                return t == typeof(byte[]) || IsSupportedType(t.GetElementType());

            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                if (def == typeof(Nullable<>))
                    return IsSupportedType(t.GetGenericArguments()[0]);

                if (def == typeof(List<>) || def == typeof(HashSet<>) ||
                    def == typeof(Queue<>) || def == typeof(Stack<>))
                    return IsSupportedType(t.GetGenericArguments()[0]);

                if (def == typeof(Dictionary<,>))
                    return IsSupportedType(t.GetGenericArguments()[0]) &&
                           IsSupportedType(t.GetGenericArguments()[1]);
            }

            return false;
        }

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

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                writer.Write((byte)ArgType.Nullable);
                bool hasValue = value != null;
                writer.Write(hasValue);
                if (hasValue)
                    WriteArg(writer, value, Nullable.GetUnderlyingType(type));
                return;
            }

            if (type.IsArray && type != typeof(byte[]))
            {
                writer.Write((byte)ArgType.Array);
                var arr = (Array)value;
                int len = arr?.Length ?? 0;
                writer.Write(len);
                if (arr != null)
                {
                    var elementType = type.GetElementType();
                    for (int i = 0; i < len; i++)
                        WriteCollectionElement(writer, arr.GetValue(i), elementType);
                }
                return;
            }

            if (type.IsGenericType)
            {
                var def = type.GetGenericTypeDefinition();

                if (def == typeof(List<>) || def == typeof(HashSet<>) ||
                    def == typeof(Queue<>) || def == typeof(Stack<>))
                {
                    var elementType = type.GetGenericArguments()[0];

                    if (def == typeof(List<>))
                        writer.Write((byte)ArgType.List);
                    else if (def == typeof(HashSet<>))
                        writer.Write((byte)ArgType.HashSet);
                    else if (def == typeof(Queue<>))
                        writer.Write((byte)ArgType.Queue);
                    else
                        writer.Write((byte)ArgType.Stack);

                    var collection = (IEnumerable)value;
                    var objs = collection?.Cast<object>().ToArray();
                    if (def == typeof(Stack<>) && objs != null)
                        Array.Reverse(objs);
                    int count = objs?.Length ?? 0;
                    writer.Write(count);
                    if (objs != null)
                    {
                        for (int i = 0; i < count; i++)
                            WriteCollectionElement(writer, objs[i], elementType);
                    }
                    return;
                }

                if (def == typeof(Dictionary<,>))
                {
                    writer.Write((byte)ArgType.Dict);
                    var dict = (IDictionary)value;
                    int count = dict?.Count ?? 0;
                    writer.Write(count);
                    if (dict != null)
                    {
                        var keyType = type.GetGenericArguments()[0];
                        var valueType = type.GetGenericArguments()[1];
                        foreach (DictionaryEntry entry in dict)
                        {
                            WriteCollectionElement(writer, entry.Key, keyType);
                            WriteCollectionElement(writer, entry.Value, valueType);
                        }
                    }
                    return;
                }
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
                case ArgType.Short:     writer.Write((short)value); break;
                case ArgType.UShort:    writer.Write((ushort)value); break;
                case ArgType.UInt:      writer.Write((uint)value); break;
                case ArgType.SByte:     writer.Write((sbyte)value); break;
                case ArgType.Char:      writer.Write((char)value); break;
                case ArgType.Decimal:   writer.Write((decimal)value); break;
                case ArgType.String: writer.Write(CompressString((string)value) ?? string.Empty); break;
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
                case ArgType.Short: return reader.ReadInt16();
                case ArgType.UShort: return reader.ReadUInt16();
                case ArgType.UInt: return reader.ReadUInt32();
                case ArgType.SByte: return reader.ReadSByte();
                case ArgType.Char: return reader.ReadChar();
                case ArgType.Decimal: return reader.ReadDecimal();
                case ArgType.String: return DecompressString(reader.ReadString());
                case ArgType.Vector2: return reader.ReadVector2();
                case ArgType.Vector3: return reader.ReadVector3();
                case ArgType.Color:
                    return new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                case ArgType.Quaternion:
                    return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                case ArgType.ByteArray:
                    return reader.ReadBytes(reader.ReadInt32());

                case ArgType.Array:
                {
                    int len = reader.ReadInt32();
                    var elementType = type.GetElementType();
                    var arr = Array.CreateInstance(elementType, len);
                    for (int i = 0; i < len; i++)
                        arr.SetValue(ReadCollectionElement(reader, elementType), i);
                    return arr;
                }

                case ArgType.List:
                {
                    int count = reader.ReadInt32();
                    var elementType = type.GetGenericArguments()[0];
                    var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
                    for (int i = 0; i < count; i++)
                        list.Add(ReadCollectionElement(reader, elementType));
                    return list;
                }

                case ArgType.Dict:
                {
                    int count = reader.ReadInt32();
                    var keyType = type.GetGenericArguments()[0];
                    var valueType = type.GetGenericArguments()[1];
                    var dict = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, valueType));
                    for (int i = 0; i < count; i++)
                    {
                        var key = ReadCollectionElement(reader, keyType);
                        var val = ReadCollectionElement(reader, valueType);
                        dict.Add(key, val);
                    }
                    return dict;
                }

                case ArgType.HashSet:
                {
                    int count = reader.ReadInt32();
                    var elementType = type.GetGenericArguments()[0];
                    var hashSetType = typeof(HashSet<>).MakeGenericType(elementType);
                    var hashSet = Activator.CreateInstance(hashSetType);
                    var addMethod = hashSetType.GetMethod("Add");
                    for (int i = 0; i < count; i++)
                        addMethod.Invoke(hashSet, new[] { ReadCollectionElement(reader, elementType) });
                    return hashSet;
                }

                case ArgType.Queue:
                {
                    int count = reader.ReadInt32();
                    var elementType = type.GetGenericArguments()[0];
                    var queueType = typeof(Queue<>).MakeGenericType(elementType);
                    var queue = Activator.CreateInstance(queueType);
                    var enqueueMethod = queueType.GetMethod("Enqueue");
                    for (int i = 0; i < count; i++)
                        enqueueMethod.Invoke(queue, new[] { ReadCollectionElement(reader, elementType) });
                    return queue;
                }

                case ArgType.Stack:
                {
                    int count = reader.ReadInt32();
                    var elementType = type.GetGenericArguments()[0];
                    var stackType = typeof(Stack<>).MakeGenericType(elementType);
                    var stack = Activator.CreateInstance(stackType);
                    var pushMethod = stackType.GetMethod("Push");
                    for (int i = 0; i < count; i++)
                        pushMethod.Invoke(stack, new[] { ReadCollectionElement(reader, elementType) });
                    return stack;
                }

                case ArgType.Nullable:
                {
                    bool hasValue = reader.ReadBoolean();
                    if (!hasValue) return null;
                    return ReadArg(reader, Nullable.GetUnderlyingType(type));
                }

                default:
                    throw new InvalidDataException($"Unknown RPC arg type tag: {tag}");
            }
        }

        private static void WriteCollectionElement(BinaryWriter writer, object value, Type elementType)
        {
            bool needsNullPrefix = !elementType.IsValueType ||
                (elementType.IsGenericType && elementType.GetGenericTypeDefinition() == typeof(Nullable<>));

            if (needsNullPrefix)
            {
                bool notNull = value != null;
                writer.Write(notNull);
                if (!notNull) return;
            }

            WriteArg(writer, value, elementType);
        }

        private static object ReadCollectionElement(BinaryReader reader, Type elementType)
        {
            bool needsNullPrefix = !elementType.IsValueType ||
                (elementType.IsGenericType && elementType.GetGenericTypeDefinition() == typeof(Nullable<>));

            if (needsNullPrefix)
            {
                bool notNull = reader.ReadBoolean();
                if (!notNull) return null;
            }

            return ReadArg(reader, elementType);
        }
        
        public static string CompressString(string text)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(text);
            var memoryStream = new MemoryStream();
            using (var gZipStream = new GZipStream(memoryStream, CompressionMode.Compress, true))
            {
                gZipStream.Write(buffer, 0, buffer.Length);
            }

            memoryStream.Position = 0;

            var compressedData = new byte[memoryStream.Length];
            memoryStream.Read(compressedData, 0, compressedData.Length);

            var gZipBuffer = new byte[compressedData.Length + 4];
            Buffer.BlockCopy(compressedData, 0, gZipBuffer, 4, compressedData.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(buffer.Length), 0, gZipBuffer, 0, 4);
            return Convert.ToBase64String(gZipBuffer);
        }
    
        public static string DecompressString(string compressedText)
        {
            try
            {
                //return compressedText.Trim('`');
                byte[] gZipBuffer = Convert.FromBase64String(compressedText);
                using (var memoryStream = new MemoryStream())
                {
                    int dataLength = BitConverter.ToInt32(gZipBuffer, 0);
                    memoryStream.Write(gZipBuffer, 4, gZipBuffer.Length - 4);

                    var buffer = new byte[dataLength];

                    memoryStream.Position = 0;
                    using (var gZipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
                    {
                        gZipStream.Read(buffer, 0, buffer.Length);
                    }

                    return Encoding.UTF8.GetString(buffer);
                }
            }
            catch (Exception ex) 
            {
                return string.Empty;
            }
        }
    }
}
