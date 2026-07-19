using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Prehistoric;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.Prehistoric
{
	public enum FossilMarkerTarget : byte
	{
		FossilBits,
		MinorFossilDigSite,
		MajorFossilDigSite
	}

	public sealed class FossilMarkerPacketData
	{
		public FossilMarkerTarget TargetKind;
		public int TargetNetId;
		public bool MarkedForDig;

		internal void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid fossil marker payload");

			writer.Write((byte)TargetKind);
			writer.Write(TargetNetId);
			writer.Write(MarkedForDig);
		}

		internal static FossilMarkerPacketData Deserialize(BinaryReader reader)
		{
			var data = new FossilMarkerPacketData
			{
				TargetKind = (FossilMarkerTarget)reader.ReadByte(),
				TargetNetId = reader.ReadInt32(),
				MarkedForDig = reader.ReadBoolean()
			};
			if (!data.IsWireValid())
				throw new InvalidDataException("Invalid fossil marker payload");
			return data;
		}

		internal bool IsWireValid()
			=> TargetNetId != 0 && TargetKind <= FossilMarkerTarget.MajorFossilDigSite;
	}

	public sealed class FossilMarkerRequestPacket : IPacket, IClientRelayable
	{
		public FossilMarkerPacketData Data = new();

		public FossilMarkerRequestPacket()
		{
		}

		internal FossilMarkerRequestPacket(FossilMarkerPacketData data)
		{
			Data = data;
		}

		public void Serialize(BinaryWriter writer) => Data.Serialize(writer);

		public void Deserialize(BinaryReader reader) => Data = FossilMarkerPacketData.Deserialize(reader);

		public void OnDispatched()
		{
			DispatchContext context = PacketHandler.CurrentContext;
			bool protocolVerified = MultiplayerSession.ConnectedPlayers.TryGetValue(
				context.SenderId, out MultiplayerPlayer player) && player.ProtocolVerified;
			if (!ShouldAccept(MultiplayerSession.IsHost, context, protocolVerified) ||
			    !FossilMarkerSync.TryApply(Data))
				return;

			PacketSender.SendToAllClients(new FossilMarkerStatePacket(Data));
		}

		internal static bool ShouldAccept(
			bool localIsHost,
			DispatchContext context,
			bool senderProtocolVerified)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast &&
			   senderProtocolVerified;
	}

	public sealed class FossilMarkerStatePacket : IPacket, IHostOnlyPacket
	{
		public FossilMarkerPacketData Data = new();

		public FossilMarkerStatePacket()
		{
		}

		internal FossilMarkerStatePacket(FossilMarkerPacketData data)
		{
			Data = data;
		}

		public void Serialize(BinaryWriter writer) => Data.Serialize(writer);

		public void Deserialize(BinaryReader reader) => Data = FossilMarkerPacketData.Deserialize(reader);

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				FossilMarkerSync.TryApply(Data);
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}
}
