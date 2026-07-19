using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using ONI_Together.Networking.Components;
using static TUNING.NOISE_POLLUTION;
using ONI_Together.Misc;
using UnityEngine;
using ONI_Together.Networking.Components.StructureStateSyncers;
using static ONI_Together.STRINGS.UI.MP_OVERLAY;
using System.Collections.Generic;
using System;
using System.Text;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.World
{
	public class StructureStatePacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		internal const int MaxOptionalValueCount = 256;
		internal const int MaxOptionalBlobBytes = 1024 * 1024;
		internal const int MaxStringLength = 4096;
		internal const int MaxByteArrayBytes = 1024 * 1024;
		private static readonly Encoding WireEncoding = new UTF8Encoding(false, true);

        public int NetId;
        public int Cell;
		public ulong Revision;
		public string SyncerTypeName = string.Empty;
		public Variant Value; // Joules for Battery, Progress for others

		public Dictionary<string, Variant> OptionalValues = []; // Extra things (such as EnergyGenerator mass, storage amount etc)

		public bool IsActive; // Operational active state

        public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			if (Revision == 0)
				Revision = NetworkIdentityRegistry.NextAuthorityRevision();
			if (string.IsNullOrEmpty(SyncerTypeName))
				SyncerTypeName = typeof(StructureStatePacket).FullName;
			int optionalBlobBytes = ValidateForSerialize();

            writer.Write(NetId);
			writer.Write(Cell);
			writer.Write(Revision);
			writer.Write(SyncerTypeName ?? string.Empty);
            Value.Write(writer);
			writer.Write(IsActive);

			using var optMs = new MemoryStream(optionalBlobBytes);
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
			ValidateIdentity(NetId, Cell);
			Revision = reader.ReadUInt64();
			SyncerTypeName = reader.ReadString();
			if (SyncerTypeName.Length == 0 || SyncerTypeName.Length > MaxStringLength)
				throw new InvalidDataException("Invalid structure syncer type name");
			if (Revision == 0)
				throw new InvalidDataException("Invalid structure state revision");
			Value = ReadVariant(reader);
			IsActive = reader.ReadBoolean();

			OptionalValues = ReadOptionalValues(reader);
        }

		internal static Dictionary<string, Variant> ReadOptionalValues(BinaryReader reader)
		{
			int optLen = reader.ReadInt32();
			if (optLen < sizeof(int) || optLen > MaxOptionalBlobBytes)
				throw new InvalidDataException($"Invalid structure optional value blob length: {optLen}");
			byte[] optBlob = reader.ReadBytes(optLen);
			if (optBlob.Length != optLen)
				throw new EndOfStreamException("Structure optional value blob is truncated");
			using var optBr = new BinaryReader(new MemoryStream(optBlob));
			int length = optBr.ReadInt32();
			if (length < 0 || length > MaxOptionalValueCount)
				throw new InvalidDataException($"Invalid structure optional value count: {length}");
			var values = new Dictionary<string, Variant>(length);
			for (int i = 0; i < length; i++)
			{
				string key = optBr.ReadString();
				if (key.Length == 0 || key.Length > MaxStringLength)
					throw new InvalidDataException("Invalid structure optional value key");
				if (values.ContainsKey(key))
					throw new InvalidDataException("Duplicate structure optional value key");
				values[key] = ReadVariant(optBr);
			}
			if (optBr.BaseStream.Position != optBr.BaseStream.Length)
				throw new InvalidDataException("Structure optional value blob contains trailing bytes");
			return values;
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
						throw new InvalidDataException("Structure variant string is too long");
					break;
				case Variant.TypeCode.Boolean: value.Boolean = reader.ReadBoolean(); break;
				case Variant.TypeCode.Vector3: value.Vector3 = reader.ReadVector3(); break;
				case Variant.TypeCode.Vector2: value.Vector2 = reader.ReadVector2(); break;
				case Variant.TypeCode.ByteArray:
					int length = reader.ReadInt32();
					if (length < 0 || length > MaxByteArrayBytes)
						throw new InvalidDataException($"Invalid structure variant byte array length: {length}");
					value.ByteArray = reader.ReadBytes(length);
					if (value.ByteArray.Length != length)
						throw new EndOfStreamException("Structure variant byte array is truncated");
					break;
				default:
					throw new InvalidDataException($"Invalid structure variant type: {(byte)value.Type}");
			}
			return value;
		}

		private int ValidateForSerialize()
		{
			if (string.IsNullOrEmpty(SyncerTypeName) || SyncerTypeName.Length > MaxStringLength)
				throw new InvalidDataException("Invalid structure syncer type name");
			ValidateVariant(Value);
			if (OptionalValues == null || OptionalValues.Count > MaxOptionalValueCount)
				throw new InvalidDataException($"Invalid structure optional value count: {OptionalValues?.Count ?? -1}");

			long blobBytes = sizeof(int);
			foreach (var entry in OptionalValues)
			{
				if (string.IsNullOrEmpty(entry.Key) || entry.Key.Length > MaxStringLength)
					throw new InvalidDataException("Invalid structure optional value key");
				blobBytes += GetStringWireBytes(entry.Key) + GetVariantWireBytes(entry.Value);
				if (blobBytes > MaxOptionalBlobBytes)
					throw new InvalidDataException($"Structure optional values exceed {MaxOptionalBlobBytes} bytes");
			}
			ValidateIdentity(NetId, Cell);
			return (int)blobBytes;
		}

		private static void ValidateIdentity(int netId, int cell)
		{
			// Deterministic identities are signed hashes; zero alone is the unassigned sentinel.
			if (netId == 0)
				throw new InvalidDataException($"Invalid structure NetId: {netId}");
			if (!Grid.IsValidCell(cell))
				throw new InvalidDataException($"Invalid structure cell: {cell}");
		}

		private static int GetVariantWireBytes(Variant value)
		{
			ValidateVariant(value);
			return value.Type switch
			{
				Variant.TypeCode.Float or Variant.TypeCode.Int => 1 + sizeof(int),
				Variant.TypeCode.Byte or Variant.TypeCode.Boolean => 2,
				Variant.TypeCode.Vector3 => 1 + 3 * sizeof(float),
				Variant.TypeCode.Vector2 => 1 + 2 * sizeof(float),
				Variant.TypeCode.String => 1 + GetStringWireBytes(value.String),
				Variant.TypeCode.ByteArray => 1 + sizeof(int) + value.ByteArray.Length,
				_ => throw new InvalidDataException($"Invalid structure variant type: {(byte)value.Type}")
			};
		}

		private static void ValidateVariant(Variant value)
		{
			if (value.Type == Variant.TypeCode.String
			    && (value.String == null || value.String.Length > MaxStringLength))
				throw new InvalidDataException("Invalid structure variant string");
			if (value.Type == Variant.TypeCode.ByteArray
			    && (value.ByteArray == null || value.ByteArray.Length > MaxByteArrayBytes))
				throw new InvalidDataException("Invalid structure variant byte array");
			if ((byte)value.Type > (byte)Variant.TypeCode.ByteArray)
				throw new InvalidDataException($"Invalid structure variant type: {(byte)value.Type}");
		}

		private static int GetStringWireBytes(string value)
		{
			try
			{
				int byteCount = WireEncoding.GetByteCount(value);
				return Get7BitEncodedIntBytes(byteCount) + byteCount;
			}
			catch (EncoderFallbackException ex)
			{
				throw new InvalidDataException("Structure string contains invalid UTF-16", ex);
			}
		}

		private static int Get7BitEncodedIntBytes(int value)
		{
			int bytes = 1;
			while ((value >>= 7) != 0)
				bytes++;
			return bytes;
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost) return;

			// Handled by StructureStateSyncer on client
            if(NetworkIdentityRegistry.TryGet(NetId, out var identity))
            {
                var syncers = identity.GetComponents<StructureSyncerBase>();
                foreach (var syncer in syncers)
                {
                    syncer.HandlePacket(this);
                }
                /*
                if(identity.TryGetComponent<StructureSyncerBase>(out var syncer))
                {
                    syncer.HandlePacket(this);
                }
                */
            }
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

		internal static bool ShouldApplyRevision(ulong current, ulong incoming)
			=> NetworkIdentityRegistry.IsNewerRevision(current, incoming);

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
