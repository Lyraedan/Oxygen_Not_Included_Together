using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Packets.DLC.SpacedOut
{
	public enum RailGunPayloadPhase : byte
	{
		Takeoff,
		Travel,
		Landing,
		Grounded
	}

	public sealed class RailGunPayloadItemData
	{
		public int NetId;
		public int PrefabHash;
		public float Mass;
		public float Temperature;
		public byte DiseaseIndex;
		public int DiseaseCount;

		internal bool IsWireValid()
			=> NetId != 0 && PrefabHash != 0 && IsFinite(Mass) && Mass > 0f && Mass <= 1_000_000_000f &&
			   IsFinite(Temperature) && Temperature >= 0f && Temperature <= 100_000f && DiseaseCount >= 0;

		private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
	}

	public sealed class RailGunPayloadStatePacket : IPacket, IHostOnlyPacket
	{
		internal const int MaxItems = 256;
		private const float MaxCoordinate = 1_000_000f;
		public int SourceRailGunNetId;
		public int PayloadNetId;
		public int Revision;
		public RailGunPayloadPhase Phase;
		public int SourceQ;
		public int SourceR;
		public int DestinationQ;
		public int DestinationR;
		public int DestinationWorld;
		public Vector3 Position;
		public float TakeoffVelocity;
		public float SourceParticles;
		public int SymbolSwapIndex = -1;
		public List<RailGunPayloadItemData> Items = new();

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid railgun payload state");
			writer.Write(SourceRailGunNetId);
			writer.Write(PayloadNetId);
			writer.Write(Revision);
			writer.Write((byte)Phase);
			writer.Write(SourceQ);
			writer.Write(SourceR);
			writer.Write(DestinationQ);
			writer.Write(DestinationR);
			writer.Write(DestinationWorld);
			writer.Write(Position.x);
			writer.Write(Position.y);
			writer.Write(Position.z);
			writer.Write(TakeoffVelocity);
			writer.Write(SourceParticles);
			writer.Write(SymbolSwapIndex);
			writer.Write(Items.Count);
			foreach (RailGunPayloadItemData item in Items)
				WriteItem(writer, item);
		}

		public void Deserialize(BinaryReader reader)
		{
			SourceRailGunNetId = reader.ReadInt32();
			PayloadNetId = reader.ReadInt32();
			Revision = reader.ReadInt32();
			Phase = (RailGunPayloadPhase)reader.ReadByte();
			SourceQ = reader.ReadInt32();
			SourceR = reader.ReadInt32();
			DestinationQ = reader.ReadInt32();
			DestinationR = reader.ReadInt32();
			DestinationWorld = reader.ReadInt32();
			Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			TakeoffVelocity = reader.ReadSingle();
			SourceParticles = reader.ReadSingle();
			SymbolSwapIndex = reader.ReadInt32();
			int count = reader.ReadInt32();
			if (count < 0 || count > MaxItems)
				throw new InvalidDataException("Invalid railgun payload item count");
			Items = new List<RailGunPayloadItemData>(count);
			for (int i = 0; i < count; i++)
				Items.Add(ReadItem(reader));
			if (!IsWireValid())
				throw new InvalidDataException("Invalid railgun payload state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				RailGunPayloadSync.TryApply(this);
		}

		internal bool IsWireValid()
		{
			if (SourceRailGunNetId == 0 || PayloadNetId == 0 || Revision < 0 || Revision > 1_000_000 ||
			    Phase < RailGunPayloadPhase.Takeoff || Phase > RailGunPayloadPhase.Grounded ||
			    !RocketSettingsPacketData.CoordinateWithinBounds(SourceQ, SourceR) ||
			    !RocketSettingsPacketData.CoordinateWithinBounds(DestinationQ, DestinationR) ||
			    DestinationWorld < -1 || !ValidVector(Position) || !IsFinite(TakeoffVelocity) ||
			    TakeoffVelocity < 0f || TakeoffVelocity > 1_000f || !IsFinite(SourceParticles) ||
			    SourceParticles < 0f || SourceParticles > 1_000_000_000f || Items == null || Items.Count > MaxItems)
				return false;
			if (SymbolSwapIndex < -1 || SymbolSwapIndex > 255)
				return false;

			var netIds = new HashSet<int>();
			foreach (RailGunPayloadItemData item in Items)
				if (item == null || !item.IsWireValid() || !netIds.Add(item.NetId))
					return false;
			return true;
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost) => !localIsHost && senderIsHost;

		private static bool ValidVector(Vector3 value)
			=> IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) &&
			   Math.Abs(value.x) <= MaxCoordinate && Math.Abs(value.y) <= MaxCoordinate &&
			   Math.Abs(value.z) <= MaxCoordinate;

		private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

		private static void WriteItem(BinaryWriter writer, RailGunPayloadItemData item)
		{
			writer.Write(item.NetId);
			writer.Write(item.PrefabHash);
			writer.Write(item.Mass);
			writer.Write(item.Temperature);
			writer.Write(item.DiseaseIndex);
			writer.Write(item.DiseaseCount);
		}

		private static RailGunPayloadItemData ReadItem(BinaryReader reader)
			=> new()
			{
				NetId = reader.ReadInt32(),
				PrefabHash = reader.ReadInt32(),
				Mass = reader.ReadSingle(),
				Temperature = reader.ReadSingle(),
				DiseaseIndex = reader.ReadByte(),
				DiseaseCount = reader.ReadInt32()
			};
	}
}
