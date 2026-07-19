using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.SpacedOut
{
	public sealed class ArtifactInventoryItemData
	{
		internal const int MaxIdLength = 256;
		internal const float MaxMass = 1_000_000_000f;
		public string Id = "";
		public float Mass;
		public Element.State State;

		internal bool IsWireValid()
			=> !string.IsNullOrEmpty(Id) && Id.Length <= MaxIdLength &&
			   IsFinite(Mass) && Mass > 0f && Mass <= MaxMass && IsValidState(State);

		private static bool IsValidState(Element.State state)
			=> state == Element.State.Vacuum || state == Element.State.Solid ||
			   state == Element.State.Liquid || state == Element.State.Gas;

		private static bool IsFinite(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value);
	}

	public sealed class ArtifactInventoryStatePacket : IPacket, IHostOnlyPacket
	{
		internal const int MaxItemCount = 256;
		public int ModuleNetId;
		public int LocationQ;
		public int LocationR;
		public List<ArtifactInventoryItemData> Items = new();

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid artifact inventory state");
			writer.Write(ModuleNetId);
			writer.Write(LocationQ);
			writer.Write(LocationR);
			writer.Write(Items.Count);
			foreach (ArtifactInventoryItemData item in Items)
			{
				writer.Write(item.Id);
				writer.Write(item.Mass);
				writer.Write((int)item.State);
			}
		}

		public void Deserialize(BinaryReader reader)
		{
			ModuleNetId = reader.ReadInt32();
			LocationQ = reader.ReadInt32();
			LocationR = reader.ReadInt32();
			int count = reader.ReadInt32();
			if (count < 0 || count > MaxItemCount)
				throw new InvalidDataException("Invalid artifact inventory item count");
			Items = new List<ArtifactInventoryItemData>(count);
			for (int i = 0; i < count; i++)
				Items.Add(ReadItem(reader));
			if (!IsWireValid())
				throw new InvalidDataException("Invalid artifact inventory state");
		}

		public void OnDispatched()
		{
			if (!MultiplayerSession.IsHost && PacketHandler.CurrentContext.SenderIsHost)
				ArtifactHarvestSync.TryApply(this);
		}

		internal bool IsWireValid()
		{
			if (ModuleNetId == 0 || Items == null || Items.Count > MaxItemCount ||
			    !RocketSettingsPacketData.CoordinateWithinBounds(LocationQ, LocationR))
				return false;
			var ids = new HashSet<string>(StringComparer.Ordinal);
			foreach (ArtifactInventoryItemData item in Items)
			{
				if (item == null || !item.IsWireValid() || !ids.Add(item.Id))
					return false;
			}
			return true;
		}

		private static ArtifactInventoryItemData ReadItem(BinaryReader reader)
		{
			string id = reader.ReadString();
			if (id.Length > ArtifactInventoryItemData.MaxIdLength)
				throw new InvalidDataException("Artifact inventory item ID is too long");
			return new ArtifactInventoryItemData
			{
				Id = id,
				Mass = reader.ReadSingle(),
				State = (Element.State)reader.ReadInt32()
			};
		}
	}
}
