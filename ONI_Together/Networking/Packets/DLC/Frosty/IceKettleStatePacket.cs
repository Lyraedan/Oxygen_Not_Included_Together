using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Frosty;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.Frosty
{
	public sealed class IceKettleItemState
	{
		internal const float MaxMass = 1_000_000f;
		public int NetId;
		public int TagHash;
		public float Mass;
		public float Temperature;
		public byte DiseaseIndex;
		public int DiseaseCount;

		internal bool IsWireValid()
			=> NetId != 0 && TagHash != 0 && ValidFinite(Mass) && Mass >= 0f && Mass <= MaxMass &&
			   ValidFinite(Temperature) && Temperature > 0f && Temperature <= 10_000f &&
			   DiseaseCount >= 0 && (DiseaseCount == 0 || DiseaseIndex != byte.MaxValue);

		private static bool ValidFinite(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value);
	}

	public sealed class IceKettleStorageState
	{
		internal const int MaxItems = 16;
		public List<IceKettleItemState> Items = new();

		internal bool IsWireValid()
		{
			if (Items == null || Items.Count > MaxItems)
				return false;
			foreach (IceKettleItemState item in Items)
			{
				if (item == null || !item.IsWireValid())
					return false;
			}
			return true;
		}
	}

	public sealed class IceKettleExhaustState
	{
		public SimHashes Element;
		public float Mass;
		public float Temperature;
		public ulong Sequence;

		internal bool IsWireValid()
			=> Element != SimHashes.Vacuum && ValidFinite(Mass) && Mass > 0f &&
			   Mass <= IceKettleItemState.MaxMass && ValidFinite(Temperature) &&
			   Temperature > 0f && Temperature <= 10_000f && Sequence > 0;

		private static bool ValidFinite(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value);
	}

	public sealed class IceKettleStatePacket : IPacket, IHostOnlyPacket
	{
		private const float MaxMeltingTimer = 1_000_000f;
		public int TargetNetId;
		public long Revision;
		public float MeltingTimer;
		public IceKettleStorageState FuelStorage = new();
		public IceKettleStorageState KettleStorage = new();
		public IceKettleStorageState OutputStorage = new();

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid ice kettle state");
			writer.Write(TargetNetId);
			writer.Write(Revision);
			writer.Write(MeltingTimer);
			WriteStorage(writer, FuelStorage);
			WriteStorage(writer, KettleStorage);
			WriteStorage(writer, OutputStorage);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			Revision = reader.ReadInt64();
			MeltingTimer = reader.ReadSingle();
			FuelStorage = ReadStorage(reader);
			KettleStorage = ReadStorage(reader);
			OutputStorage = ReadStorage(reader);
			if (!IsWireValid())
				throw new InvalidDataException("Invalid ice kettle state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				IceKettleSync.TryApply(this);
		}

		internal bool IsWireValid()
		{
			if (TargetNetId == 0 || Revision <= 0 || !ValidFinite(MeltingTimer) || MeltingTimer < 0f ||
			    MeltingTimer > MaxMeltingTimer || FuelStorage == null || KettleStorage == null ||
			    OutputStorage == null || !FuelStorage.IsWireValid() || !KettleStorage.IsWireValid() ||
			    !OutputStorage.IsWireValid())
				return false;
			var ids = new HashSet<int>();
			return AddIds(ids, FuelStorage) && AddIds(ids, KettleStorage) && AddIds(ids, OutputStorage);
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		private static bool AddIds(HashSet<int> ids, IceKettleStorageState storage)
		{
			foreach (IceKettleItemState item in storage.Items)
			{
				if (!ids.Add(item.NetId))
					return false;
			}
			return true;
		}

		private static void WriteStorage(BinaryWriter writer, IceKettleStorageState storage)
		{
			writer.Write((byte)storage.Items.Count);
			foreach (IceKettleItemState item in storage.Items)
			{
				writer.Write(item.NetId);
				writer.Write(item.TagHash);
				writer.Write(item.Mass);
				writer.Write(item.Temperature);
				writer.Write(item.DiseaseIndex);
				writer.Write(item.DiseaseCount);
			}
		}

		private static IceKettleStorageState ReadStorage(BinaryReader reader)
		{
			int count = reader.ReadByte();
			if (count > IceKettleStorageState.MaxItems)
				throw new InvalidDataException("Invalid ice kettle storage item count");
			var storage = new IceKettleStorageState { Items = new List<IceKettleItemState>(count) };
			for (int i = 0; i < count; i++)
			{
				storage.Items.Add(new IceKettleItemState
				{
					NetId = reader.ReadInt32(),
					TagHash = reader.ReadInt32(),
					Mass = reader.ReadSingle(),
					Temperature = reader.ReadSingle(),
					DiseaseIndex = reader.ReadByte(),
					DiseaseCount = reader.ReadInt32()
				});
			}
			return storage;
		}

		private static bool ValidFinite(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value);
	}

	public sealed class IceKettleExhaustPacket : IPacket, IHostOnlyPacket
	{
		public int TargetNetId;
		public IceKettleExhaustState Exhaust = new();

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid ice kettle exhaust event");
			writer.Write(TargetNetId);
			writer.Write((int)Exhaust.Element);
			writer.Write(Exhaust.Mass);
			writer.Write(Exhaust.Temperature);
			writer.Write(Exhaust.Sequence);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			Exhaust = new IceKettleExhaustState
			{
				Element = (SimHashes)reader.ReadInt32(),
				Mass = reader.ReadSingle(),
				Temperature = reader.ReadSingle(),
				Sequence = reader.ReadUInt64()
			};
			if (!IsWireValid())
				throw new InvalidDataException("Invalid ice kettle exhaust event");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				IceKettleSync.TryApplyExhaust(this);
		}

		internal bool IsWireValid()
			=> TargetNetId != 0 && Exhaust != null && Exhaust.IsWireValid();

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}
}
