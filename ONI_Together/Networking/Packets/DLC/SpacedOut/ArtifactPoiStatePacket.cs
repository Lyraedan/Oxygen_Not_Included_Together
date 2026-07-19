using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.SpacedOut
{
	public sealed class ArtifactSelectorStateData
	{
		internal const int MaxIds = 256;
		internal const int MaxIdLength = 256;
		public List<string> Terrestrial = new();
		public List<string> Space = new();
		public List<string> Any = new();
		public int AnalyzedTerrestrialCount;
		public int AnalyzedSpaceCount;
		public List<string> AnalyzedIds = new();

		internal void Serialize(BinaryWriter writer)
		{
			WriteList(writer, Terrestrial);
			WriteList(writer, Space);
			WriteList(writer, Any);
			writer.Write(AnalyzedTerrestrialCount);
			writer.Write(AnalyzedSpaceCount);
			WriteList(writer, AnalyzedIds);
		}

		internal static ArtifactSelectorStateData Deserialize(BinaryReader reader)
			=> new()
			{
				Terrestrial = ReadList(reader),
				Space = ReadList(reader),
				Any = ReadList(reader),
				AnalyzedTerrestrialCount = reader.ReadInt32(),
				AnalyzedSpaceCount = reader.ReadInt32(),
				AnalyzedIds = ReadList(reader)
			};

		internal bool IsWireValid()
			=> ValidList(Terrestrial) && ValidList(Space) && ValidList(Any) && ValidList(AnalyzedIds) &&
			   AnalyzedTerrestrialCount >= 0 && AnalyzedTerrestrialCount <= MaxIds &&
			   AnalyzedSpaceCount >= 0 && AnalyzedSpaceCount <= MaxIds;

		private static void WriteList(BinaryWriter writer, List<string> ids)
		{
			if (!ValidList(ids))
				throw new InvalidDataException("Invalid artifact selector IDs");
			writer.Write(ids.Count);
			foreach (string id in ids)
				writer.Write(id);
		}

		private static List<string> ReadList(BinaryReader reader)
		{
			int count = reader.ReadInt32();
			if (count < 0 || count > MaxIds)
				throw new InvalidDataException("Invalid artifact selector ID count");
			var ids = new List<string>(count);
			for (int i = 0; i < count; i++)
			{
				string id = reader.ReadString();
				if (!ValidId(id))
					throw new InvalidDataException("Invalid artifact selector ID");
				ids.Add(id);
			}
			return ids;
		}

		private static bool ValidList(List<string> ids)
		{
			if (ids == null || ids.Count > MaxIds)
				return false;
			foreach (string id in ids)
				if (!ValidId(id))
					return false;
			return true;
		}

		private static bool ValidId(string id)
			=> !string.IsNullOrEmpty(id) && id.Length <= MaxIdLength;
	}

	public sealed class ArtifactPoiStatePacket : IPacket, IHostOnlyPacket
	{
		private const int MaxHarvests = 1_000_000;
		public int TargetNetId;
		public ulong LifecycleRevision;
		public int LocationQ;
		public int LocationR;
		public float PoiCharge;
		public int NumHarvests;
		public string ArtifactToHarvest = "";
		public List<ArtifactInventoryItemData> Items = new();
		public ArtifactSelectorStateData Selector = new();

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid artifact POI state");
			writer.Write(TargetNetId);
			writer.Write(LifecycleRevision);
			writer.Write(LocationQ);
			writer.Write(LocationR);
			writer.Write(PoiCharge);
			writer.Write(NumHarvests);
			writer.Write(ArtifactToHarvest ?? "");
			writer.Write(Items.Count);
			foreach (ArtifactInventoryItemData item in Items)
			{
				writer.Write(item.Id);
				writer.Write(item.Mass);
				writer.Write((int)item.State);
			}
			Selector.Serialize(writer);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			LifecycleRevision = reader.ReadUInt64();
			LocationQ = reader.ReadInt32();
			LocationR = reader.ReadInt32();
			PoiCharge = reader.ReadSingle();
			NumHarvests = reader.ReadInt32();
			ArtifactToHarvest = reader.ReadString();
			Items = ReadItems(reader);
			Selector = ArtifactSelectorStateData.Deserialize(reader);
			if (!IsWireValid())
				throw new InvalidDataException("Invalid artifact POI state");
		}

		public void OnDispatched()
		{
			if (!MultiplayerSession.IsHost && PacketHandler.CurrentContext.SenderIsHost)
				ArtifactPoiSync.TryApply(this);
		}

		internal bool IsWireValid()
		{
			if (TargetNetId == 0 || LifecycleRevision == 0 ||
			    !RocketSettingsPacketData.CoordinateWithinBounds(LocationQ, LocationR) ||
			    !IsFinite(PoiCharge) || PoiCharge < 0f || PoiCharge > 1f ||
			    NumHarvests < 0 || NumHarvests > MaxHarvests || Items == null ||
			    Items.Count > ArtifactInventoryStatePacket.MaxItemCount || Selector?.IsWireValid() != true)
				return false;
			if (ArtifactToHarvest != null && ArtifactToHarvest.Length > ArtifactSelectorStateData.MaxIdLength)
				return false;
			foreach (ArtifactInventoryItemData item in Items)
				if (item?.IsWireValid() != true)
					return false;
			return true;
		}

		private static List<ArtifactInventoryItemData> ReadItems(BinaryReader reader)
		{
			int count = reader.ReadInt32();
			if (count < 0 || count > ArtifactInventoryStatePacket.MaxItemCount)
				throw new InvalidDataException("Invalid artifact POI item count");
			var items = new List<ArtifactInventoryItemData>(count);
			for (int i = 0; i < count; i++)
				items.Add(new ArtifactInventoryItemData
				{
					Id = reader.ReadString(),
					Mass = reader.ReadSingle(),
					State = (Element.State)reader.ReadInt32()
				});
			return items;
		}

		private static bool IsFinite(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
