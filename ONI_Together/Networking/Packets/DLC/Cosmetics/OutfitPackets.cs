using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Cosmetics;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.Cosmetics
{
	public enum OutfitTargetKind : byte
	{
		Minion,
		Template
	}

	public sealed class OutfitStateData
	{
		internal const int MaxItemCount = 16;
		internal const int MaxOutfitIdLength = 128;
		internal const int MaxItemIdLength = 256;
		public OutfitTargetKind TargetKind;
		public int TargetNetId;
		public string OutfitId = "";
		public ClothingOutfitUtility.OutfitType OutfitType;
		public List<string> ItemIds = new();

		internal bool IsWireValid()
		{
			if (TargetKind > OutfitTargetKind.Template ||
			    (int)OutfitType < 0 || OutfitType >= ClothingOutfitUtility.OutfitType.LENGTH ||
			    ItemIds == null || ItemIds.Count > MaxItemCount)
				return false;
			if (TargetKind == OutfitTargetKind.Minion)
			{
				if (TargetNetId == 0 || !string.IsNullOrEmpty(OutfitId))
					return false;
			}
			else if (TargetNetId != 0 || string.IsNullOrEmpty(OutfitId) || OutfitId.Length > MaxOutfitIdLength)
				return false;

			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (string itemId in ItemIds)
			{
				if (string.IsNullOrEmpty(itemId) || itemId.Length > MaxItemIdLength || !seen.Add(itemId))
					return false;
			}
			return true;
		}

		internal void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid outfit state");
			writer.Write((byte)TargetKind);
			writer.Write(TargetNetId);
			writer.Write(OutfitId);
			writer.Write((byte)OutfitType);
			writer.Write(ItemIds.Count);
			foreach (string itemId in ItemIds)
				writer.Write(itemId);
		}

		internal static OutfitStateData Deserialize(BinaryReader reader)
		{
			var data = new OutfitStateData
			{
				TargetKind = (OutfitTargetKind)reader.ReadByte(),
				TargetNetId = reader.ReadInt32(),
				OutfitId = reader.ReadString(),
				OutfitType = (ClothingOutfitUtility.OutfitType)reader.ReadByte()
			};
			if (data.OutfitId.Length > MaxOutfitIdLength)
				throw new InvalidDataException("Outfit ID is too long");
			int count = reader.ReadInt32();
			if (count < 0 || count > MaxItemCount)
				throw new InvalidDataException("Invalid outfit item count");
			data.ItemIds = new List<string>(count);
			for (int i = 0; i < count; i++)
			{
				string itemId = reader.ReadString();
				if (itemId.Length > MaxItemIdLength)
					throw new InvalidDataException("Outfit item ID is too long");
				data.ItemIds.Add(itemId);
			}
			if (!data.IsWireValid())
				throw new InvalidDataException("Invalid outfit state");
			return data;
		}
	}

	public sealed class OutfitWriteRequestPacket : IPacket, IClientRelayable
	{
		public OutfitStateData Data = new();

		public OutfitWriteRequestPacket()
		{
		}

		internal OutfitWriteRequestPacket(OutfitStateData data) => Data = data;

		public void Serialize(BinaryWriter writer) => Data.Serialize(writer);
		public void Deserialize(BinaryReader reader) => Data = OutfitStateData.Deserialize(reader);

		public void OnDispatched()
		{
			DispatchContext context = PacketHandler.CurrentContext;
			bool protocolVerified = MultiplayerSession.GetPlayer(context.SenderId)?.ProtocolVerified == true;
			if (!ShouldAccept(MultiplayerSession.IsHost, context, protocolVerified) ||
			    !OutfitWriteSync.TryApply(Data) ||
			    !OutfitWriteSync.TryResolveTarget(Data, createTemplate: false, out ClothingOutfitTarget target) ||
			    !OutfitWriteSync.TryCapture(target, out OutfitStateData state))
				return;
			PacketSender.SendToAllClients(new OutfitStatePacket(state));
		}

		internal static bool ShouldAccept(bool localIsHost, DispatchContext context, bool protocolVerified)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast && protocolVerified;
	}

	public sealed class OutfitStatePacket : IPacket, IHostOnlyPacket
	{
		public OutfitStateData Data = new();

		public OutfitStatePacket()
		{
		}

		internal OutfitStatePacket(OutfitStateData data) => Data = data;

		public void Serialize(BinaryWriter writer) => Data.Serialize(writer);
		public void Deserialize(BinaryReader reader) => Data = OutfitStateData.Deserialize(reader);

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				OutfitWriteSync.TryApply(Data);
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}
}
