using ONI_Together.Misc;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World
{
    public class LogicStatePacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
    {
        internal const int MaxOptionalValueCount = 256;
        internal const int MaxOptionalBlobBytes = 1024 * 1024;
        private const int MaxStringLength = 4096;
        private const int MaxByteArrayBytes = 1024 * 1024;
        public int NetId;
        public int Cell;
        public Variant Value;
        public Dictionary<string, Variant> OptionalValues = [];
        public bool IsActive;

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();

            writer.Write(NetId);
            writer.Write(Cell);
            Value.Write(writer);
            writer.Write(IsActive);

            using var optMs = new MemoryStream();
            using var optBw = new BinaryWriter(optMs);
            optBw.Write(OptionalValues.Count);
            foreach (var kvp in OptionalValues)
            {
                optBw.Write(kvp.Key);
                kvp.Value.Write(optBw);
            }
            writer.Write((int)optMs.Length);
            writer.Write(optMs.GetBuffer(), 0, (int)optMs.Length);
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();

            NetId = reader.ReadInt32();
            Cell = reader.ReadInt32();
            Value = ReadVariant(reader);
            IsActive = reader.ReadBoolean();

            int optLen = reader.ReadInt32();
            if (optLen < sizeof(int) || optLen > MaxOptionalBlobBytes)
                throw new InvalidDataException($"Invalid logic optional value blob length: {optLen}");
            byte[] optBlob = reader.ReadBytes(optLen);
            if (optBlob.Length != optLen)
                throw new EndOfStreamException("Logic optional value blob is truncated");
            using var optBr = new BinaryReader(new MemoryStream(optBlob));
            int length = optBr.ReadInt32();
            if (length < 0 || length > MaxOptionalValueCount)
                throw new InvalidDataException($"Invalid logic optional value count: {length}");
            OptionalValues = new Dictionary<string, Variant>(length);
            for (int i = 0; i < length; i++)
            {
                string key = optBr.ReadString();
                if (key.Length > MaxStringLength)
                    throw new InvalidDataException("Logic optional value key is too long");
                OptionalValues[key] = ReadVariant(optBr);
            }
            if (optBr.BaseStream.Position != optBr.BaseStream.Length)
                throw new InvalidDataException("Logic optional value blob contains trailing bytes");
        }

        private static Variant ReadVariant(BinaryReader reader)
        {
            var value = new Variant { Type = (Variant.TypeCode)reader.ReadByte() };
            switch (value.Type)
            {
                case Variant.TypeCode.Float: value.Float = reader.ReadSingle(); break;
                case Variant.TypeCode.Int: value.Int = reader.ReadInt32(); break;
                case Variant.TypeCode.Byte: value.Byte = reader.ReadByte(); break;
                case Variant.TypeCode.String:
                    value.String = reader.ReadString();
                    if (value.String.Length > MaxStringLength)
                        throw new InvalidDataException("Logic variant string is too long");
                    break;
                case Variant.TypeCode.Boolean: value.Boolean = reader.ReadBoolean(); break;
                case Variant.TypeCode.Vector3: value.Vector3 = reader.ReadVector3(); break;
                case Variant.TypeCode.Vector2: value.Vector2 = reader.ReadVector2(); break;
                case Variant.TypeCode.ByteArray:
                    int length = reader.ReadInt32();
                    if (length < 0 || length > MaxByteArrayBytes)
                        throw new InvalidDataException($"Invalid logic variant byte array length: {length}");
                    value.ByteArray = reader.ReadBytes(length);
                    if (value.ByteArray.Length != length)
                        throw new EndOfStreamException("Logic variant byte array is truncated");
                    break;
                default:
                    throw new InvalidDataException($"Invalid logic variant type: {(byte)value.Type}");
            }
            return value;
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (MultiplayerSession.IsHost) return;

            LogicStateSyncer.Instance?.HandlePacket(this);
        }

        public static bool VariantValueChanged(Variant a, Variant b, float epsilon = 0.01f)
        {
            if (a.Type != b.Type) return true;
            switch (a.Type)
            {
                case Variant.TypeCode.Float:
                    if (Mathf.Abs(a.Float - b.Float) > epsilon) return true;
                    break;
                case Variant.TypeCode.Int:
                    if (a.Int != b.Int) return true;
                    break;
                case Variant.TypeCode.Byte:
                    if (a.Byte != b.Byte) return true;
                    break;
                case Variant.TypeCode.String:
                    if (a.String != b.String) return true;
                    break;
                case Variant.TypeCode.Boolean:
                    if (a.Boolean != b.Boolean) return true;
                    break;
                case Variant.TypeCode.ByteArray:
                    if (!ByteArraysEqual(a.ByteArray, b.ByteArray)) return true;
                    break;
            }
            return false;
        }

        private static bool ByteArraysEqual(byte[] a, byte[] b)
        {
            if (a == b) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        public static bool OptionalValuesChanged(Dictionary<string, Variant> a, Dictionary<string, Variant> b)
        {
            if (a == null && b == null) return false;
            if (a == null || b == null) return true;
            if (a.Count != b.Count) return true;
            foreach (var kvp in a)
            {
                if (!b.TryGetValue(kvp.Key, out var bVal)) return true;
                if (VariantValueChanged(kvp.Value, bVal)) return true;
            }
            return false;
        }
    }
}
