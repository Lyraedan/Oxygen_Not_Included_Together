using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Common;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.Common
{
	public enum PoiTechRequestKind : byte
	{
		SetPendingChore,
		AcknowledgeNotification
	}

	public sealed class PoiTechRequestPacket : IPacket, IClientRelayable
	{
		public int TargetNetId;
		public PoiTechRequestKind Kind;
		public bool ExpectedValue;
		public bool DesiredValue;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid POI tech request");
			writer.Write(TargetNetId);
			writer.Write((byte)Kind);
			writer.Write(ExpectedValue);
			writer.Write(DesiredValue);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			Kind = (PoiTechRequestKind)reader.ReadByte();
			ExpectedValue = reader.ReadBoolean();
			DesiredValue = reader.ReadBoolean();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid POI tech request");
		}

		public void OnDispatched()
		{
			if (!ShouldAccept(MultiplayerSession.IsHost, PacketHandler.CurrentContext) ||
			    !PoiTechSync.TryApplyRequest(this))
				return;
			PoiTechSync.Broadcast(TargetNetId);
		}

		internal bool IsWireValid()
			=> TargetNetId != 0 && Kind <= PoiTechRequestKind.AcknowledgeNotification &&
			   ExpectedValue != DesiredValue &&
			   (Kind != PoiTechRequestKind.AcknowledgeNotification ||
			    (!ExpectedValue && DesiredValue));

		internal static bool ShouldAccept(bool localIsHost, DispatchContext context)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast;
	}

	public sealed class PoiTechStatePacket : IPacket, IHostOnlyPacket
	{
		public int TargetNetId;
		public bool IsUnlocked;
		public bool PendingChore;
		public bool SeenNotification;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid POI tech state");
			writer.Write(TargetNetId);
			writer.Write(IsUnlocked);
			writer.Write(PendingChore);
			writer.Write(SeenNotification);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			IsUnlocked = reader.ReadBoolean();
			PendingChore = reader.ReadBoolean();
			SeenNotification = reader.ReadBoolean();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid POI tech state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				PoiTechSync.TryApply(this);
		}

		internal bool IsWireValid()
			=> TargetNetId != 0 && (!IsUnlocked || !PendingChore) &&
			   (IsUnlocked || !SeenNotification);

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}
}
