using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Cosmetics;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.Cosmetics
{
	public enum OutfitTemplateMutationKind : byte
	{
		Rename,
		Delete
	}

	public sealed class OutfitTemplateMutationData
	{
		internal const int MaxIdLength = 128;
		public OutfitTemplateMutationKind Kind;
		public ClothingOutfitUtility.OutfitType OutfitType;
		public string OutfitId = "";
		public string NewOutfitId = "";

		internal bool IsWireValid()
		{
			if (Kind > OutfitTemplateMutationKind.Delete ||
			    (int)OutfitType < 0 || OutfitType >= ClothingOutfitUtility.OutfitType.LENGTH ||
			    string.IsNullOrEmpty(OutfitId) || OutfitId.Length > MaxIdLength)
				return false;
			return Kind == OutfitTemplateMutationKind.Delete
				? string.IsNullOrEmpty(NewOutfitId)
				: !string.IsNullOrEmpty(NewOutfitId) && NewOutfitId.Length <= MaxIdLength &&
				  NewOutfitId != OutfitId;
		}

		internal void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid outfit template mutation");
			writer.Write((byte)Kind);
			writer.Write((byte)OutfitType);
			writer.Write(OutfitId);
			writer.Write(NewOutfitId);
		}

		internal static OutfitTemplateMutationData Deserialize(BinaryReader reader)
		{
			var data = new OutfitTemplateMutationData
			{
				Kind = (OutfitTemplateMutationKind)reader.ReadByte(),
				OutfitType = (ClothingOutfitUtility.OutfitType)reader.ReadByte(),
				OutfitId = reader.ReadString(),
				NewOutfitId = reader.ReadString()
			};
			if (!data.IsWireValid())
				throw new InvalidDataException("Invalid outfit template mutation");
			return data;
		}
	}

	public sealed class OutfitTemplateMutationRequestPacket : IPacket, IClientRelayable
	{
		public OutfitTemplateMutationData Data = new();

		public OutfitTemplateMutationRequestPacket()
		{
		}

		internal OutfitTemplateMutationRequestPacket(OutfitTemplateMutationData data) => Data = data;

		public void Serialize(BinaryWriter writer) => Data.Serialize(writer);
		public void Deserialize(BinaryReader reader) => Data = OutfitTemplateMutationData.Deserialize(reader);

		public void OnDispatched()
		{
			DispatchContext context = PacketHandler.CurrentContext;
			bool verified = MultiplayerSession.GetPlayer(context.SenderId)?.ProtocolVerified == true;
			if (!ShouldAccept(MultiplayerSession.IsHost, context, verified) ||
			    !OutfitTemplateMutationSync.TryApply(Data))
				return;
			PacketSender.SendToAllClients(new OutfitTemplateMutationStatePacket(Data));
		}

		internal static bool ShouldAccept(bool localIsHost, DispatchContext context, bool verified)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast && verified;
	}

	public sealed class OutfitTemplateMutationStatePacket : IPacket, IHostOnlyPacket
	{
		public OutfitTemplateMutationData Data = new();

		public OutfitTemplateMutationStatePacket()
		{
		}

		internal OutfitTemplateMutationStatePacket(OutfitTemplateMutationData data) => Data = data;

		public void Serialize(BinaryWriter writer) => Data.Serialize(writer);
		public void Deserialize(BinaryReader reader) => Data = OutfitTemplateMutationData.Deserialize(reader);

		public void OnDispatched()
		{
			if (!MultiplayerSession.IsHost && PacketHandler.CurrentContext.SenderIsHost)
				OutfitTemplateMutationSync.TryApply(Data);
		}
	}
}
