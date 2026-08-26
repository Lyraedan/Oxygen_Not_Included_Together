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
        public const int MaxPayloadBytes = 8 * 1024 * 1024;
        private const int MaxCollectionElements = 65536;

        private enum ArgType : byte
        {
            Int, Float, Bool, Byte, Long, Double, String,
            Vector2, Vector3, Color, Quaternion, ByteArray, ULong,
            Array, List, Dict, Short, UShort, UInt, SByte, Char, Decimal,
            Nullable, HashSet, Queue, Stack, HashedString, KAnimHashedString,
            Null
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
            [typeof(HashedString)] = ArgType.HashedString,
            [typeof(KAnimHashedString)] = ArgType.KAnimHashedString,
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
            if (args == null) throw new ArgumentNullException(nameof(args));
            if (argTypes == null) throw new ArgumentNullException(nameof(argTypes));
            if (args.Length != argTypes.Length)
                throw new ArgumentException(
                    $"RPC argument count mismatch: received {args.Length}, expected {argTypes.Length}.");

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            for (int i = 0; i < argTypes.Length; i++)
            {
                if (!IsSupportedType(argTypes[i]))
                    throw new NotSupportedException($"RPC argument type '{argTypes[i]}' is not supported.");
                WriteArg(writer, args[i], argTypes[i]);
                if (ms.Length > MaxPayloadBytes)
                    throw new InvalidDataException($"RPC payload exceeds {MaxPayloadBytes} bytes.");
            }

            return ms.ToArray();
        }

        public static object[] Deserialize(byte[] data, Type[] argTypes)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (argTypes == null) throw new ArgumentNullException(nameof(argTypes));
            if (data.Length > MaxPayloadBytes)
                throw new InvalidDataException($"RPC payload exceeds {MaxPayloadBytes} bytes.");

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
                Type underlyingType = Enum.GetUnderlyingType(type);
                WriteArg(writer, Convert.ChangeType(value, underlyingType), underlyingType);
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

            if (value == null && !type.IsValueType)
            {
                writer.Write((byte)ArgType.Null);
                return;
            }

            if (value == null)
                throw new InvalidDataException($"RPC value for non-nullable type '{type}' is null.");

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
                case ArgType.HashedString:
                    writer.Write(((HashedString)value).hash);
                    break;
                case ArgType.KAnimHashedString:
                    writer.Write(((KAnimHashedString)value).hash);
                    break;
            }
        }

        private static object ReadArg(BinaryReader reader, Type type)
        {
            ArgType tag = (ArgType)reader.ReadByte();

            switch (tag)
            {
                case ArgType.Null:
                    if (type.IsValueType && Nullable.GetUnderlyingType(type) == null)
                        throw new InvalidDataException($"RPC null cannot be assigned to '{type}'.");
                    return null;

                case ArgType.Int:
                    int intVal = reader.ReadInt32();
                    return type.IsEnum ? Enum.ToObject(type, intVal) : intVal;

                case ArgType.Float: return reader.ReadSingle();
                case ArgType.Bool: return reader.ReadBoolean();
                case ArgType.Byte:
                    byte byteVal = reader.ReadByte();
                    return type.IsEnum ? Enum.ToObject(type, byteVal) : byteVal;
                case ArgType.Long:
                    long longVal = reader.ReadInt64();
                    return type.IsEnum ? Enum.ToObject(type, longVal) : longVal;
                case ArgType.ULong:
                    ulong ulongVal = reader.ReadUInt64();
                    return type.IsEnum ? Enum.ToObject(type, ulongVal) : ulongVal;
                case ArgType.Double: return reader.ReadDouble();
                case ArgType.Short:
                    short shortVal = reader.ReadInt16();
                    return type.IsEnum ? Enum.ToObject(type, shortVal) : shortVal;
                case ArgType.UShort:
                    ushort ushortVal = reader.ReadUInt16();
                    return type.IsEnum ? Enum.ToObject(type, ushortVal) : ushortVal;
                case ArgType.UInt:
                    uint uintVal = reader.ReadUInt32();
                    return type.IsEnum ? Enum.ToObject(type, uintVal) : uintVal;
                case ArgType.SByte:
                    sbyte sbyteVal = reader.ReadSByte();
                    return type.IsEnum ? Enum.ToObject(type, sbyteVal) : sbyteVal;
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
                    return ReadBytesChecked(reader);

                case ArgType.Array:
                {
                    int len = ReadCollectionCount(reader);
                    var elementType = type.GetElementType();
                    var arr = Array.CreateInstance(elementType, len);
                    for (int i = 0; i < len; i++)
                        arr.SetValue(ReadCollectionElement(reader, elementType), i);
                    return arr;
                }

                case ArgType.List:
                {
                    int count = ReadCollectionCount(reader);
                    var elementType = type.GetGenericArguments()[0];
                    var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
                    for (int i = 0; i < count; i++)
                        list.Add(ReadCollectionElement(reader, elementType));
                    return list;
                }

                case ArgType.Dict:
                {
                    int count = ReadCollectionCount(reader);
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
                    int count = ReadCollectionCount(reader);
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
                    int count = ReadCollectionCount(reader);
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
                    int count = ReadCollectionCount(reader);
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

                case ArgType.HashedString:
                    return new HashedString(reader.ReadInt32());

                case ArgType.KAnimHashedString:
                    return new KAnimHashedString(reader.ReadInt32());

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

        public static byte[] ReadPayload(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > MaxPayloadBytes)
                throw new InvalidDataException($"Invalid RPC payload length: {length}.");

            byte[] payload = reader.ReadBytes(length);
            if (payload.Length != length)
                throw new EndOfStreamException(
                    $"RPC payload ended after {payload.Length} of {length} bytes.");
            return payload;
        }

        public static void WritePayload(BinaryWriter writer, byte[] payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            if (payload.Length > MaxPayloadBytes)
                throw new InvalidDataException($"RPC payload exceeds {MaxPayloadBytes} bytes.");

            writer.Write(payload.Length);
            writer.Write(payload);
        }

        private static byte[] ReadBytesChecked(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > MaxPayloadBytes)
                throw new InvalidDataException($"Invalid byte array length: {length}.");

            byte[] value = reader.ReadBytes(length);
            if (value.Length != length)
                throw new EndOfStreamException(
                    $"Byte array ended after {value.Length} of {length} bytes.");
            return value;
        }

        private static int ReadCollectionCount(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > MaxCollectionElements)
                throw new InvalidDataException($"Invalid RPC collection count: {count}.");
            return count;
        }
        
        public static string CompressString(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            
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
            if (string.IsNullOrEmpty(compressedText)) return string.Empty;
            
            try
            {
                //return compressedText.Trim('`');
                byte[] gZipBuffer = Convert.FromBase64String(compressedText);
                if (gZipBuffer.Length < 4)
                    throw new InvalidDataException("Compressed RPC string is missing its length header.");

                int dataLength = BitConverter.ToInt32(gZipBuffer, 0);
                if (dataLength < 0 || dataLength > MaxPayloadBytes)
                    throw new InvalidDataException($"Invalid decompressed RPC string length: {dataLength}.");

                using (var memoryStream = new MemoryStream(gZipBuffer, 4, gZipBuffer.Length - 4, false))
                {
                    using (var gZipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
                    using (var output = new MemoryStream(dataLength))
                    {
                        var chunk = new byte[8192];
                        int total = 0;
                        int read;
                        while ((read = gZipStream.Read(chunk, 0, chunk.Length)) > 0)
                        {
                            total += read;
                            if (total > dataLength || total > MaxPayloadBytes)
                                throw new InvalidDataException("Compressed RPC string exceeds its declared length.");
                            output.Write(chunk, 0, read);
                        }

                        if (total != dataLength)
                            throw new InvalidDataException(
                                $"Compressed RPC string ended after {total} of {dataLength} bytes.");
                        return Encoding.UTF8.GetString(output.ToArray());
                    }
                }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Invalid compressed RPC string payload.", ex);
            }
        }
    }
}
