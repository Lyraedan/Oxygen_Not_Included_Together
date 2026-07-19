using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.SpacedOut
{
	public sealed class TemporalTearRequestPacket : IPacket, IClientRelayable
	{
		public int OpenerNetId;

		public void Serialize(BinaryWriter writer)
		{
			if (OpenerNetId == 0)
				throw new InvalidDataException("Invalid temporal tear opener");
			writer.Write(OpenerNetId);
		}

		public void Deserialize(BinaryReader reader)
		{
			OpenerNetId = reader.ReadInt32();
			if (OpenerNetId == 0)
				throw new InvalidDataException("Invalid temporal tear opener");
		}

		public void OnDispatched()
		{
			if (ShouldAccept(MultiplayerSession.IsHost, PacketHandler.CurrentContext))
				TemporalTearSync.TryFire(OpenerNetId);
		}

		internal static bool ShouldAccept(bool localIsHost, DispatchContext context)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast;
	}

	public sealed class TemporalTearStatePacket : IPacket, IHostOnlyPacket
	{
		public int LocationQ;
		public int LocationR;
		public bool Revealed;
		public bool Open;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid temporal tear state");
			writer.Write(LocationQ);
			writer.Write(LocationR);
			writer.Write(Revealed);
			writer.Write(Open);
		}

		public void Deserialize(BinaryReader reader)
		{
			LocationQ = reader.ReadInt32();
			LocationR = reader.ReadInt32();
			Revealed = reader.ReadBoolean();
			Open = reader.ReadBoolean();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid temporal tear state");
		}

		public void OnDispatched()
		{
			if (!MultiplayerSession.IsHost && PacketHandler.CurrentContext.SenderIsHost)
				TemporalTearSync.TryApply(this);
		}

		internal bool IsWireValid()
			=> RocketSettingsPacketData.CoordinateWithinBounds(LocationQ, LocationR) && (!Open || Revealed);

		internal static bool NeedsApply(bool currentRevealed, bool currentOpen, TemporalTearStatePacket state)
			=> currentRevealed != state.Revealed || currentOpen != state.Open;
	}
}
