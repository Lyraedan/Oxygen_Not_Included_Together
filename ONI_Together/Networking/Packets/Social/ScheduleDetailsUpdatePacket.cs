using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using System.IO;

namespace ONI_Together.Networking.Packets.Social
{
	public sealed class ScheduleDetailsUpdatePacket : IPacket, IClientRelayable
	{
		public enum DetailsUpdateType : byte
		{
			NAME,
			ALARM_STATE
		}

		public ulong ClientRequestId;
		public long BaseRevision;
		public int ScheduleIndex;
		public string Name = string.Empty;
		public bool AlarmActivated;
		public DetailsUpdateType UpdateType;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid schedule details request");
			writer.Write(ClientRequestId);
			writer.Write(BaseRevision);
			writer.Write(ScheduleIndex);
			writer.Write((byte)UpdateType);
			if (UpdateType == DetailsUpdateType.NAME)
				ScheduleSyncProtocol.WriteString(writer, Name, ScheduleSyncProtocol.MaxScheduleNameLength);
			else
				writer.Write(AlarmActivated);
		}

		public void Deserialize(BinaryReader reader)
		{
			ClientRequestId = reader.ReadUInt64();
			BaseRevision = reader.ReadInt64();
			ScheduleIndex = reader.ReadInt32();
			UpdateType = (DetailsUpdateType)reader.ReadByte();
			if (UpdateType == DetailsUpdateType.NAME)
				Name = ScheduleSyncProtocol.ReadString(reader, ScheduleSyncProtocol.MaxScheduleNameLength);
			else if (UpdateType == DetailsUpdateType.ALARM_STATE)
				AlarmActivated = reader.ReadBoolean();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid schedule details request");
		}

		public void OnDispatched()
		{
			ScheduleSyncCoordinator.Handle(this);
		}

		internal bool IsWireValid()
			=> ClientRequestId != 0 && BaseRevision >= 0 &&
			   ScheduleIndex >= 0 && ScheduleIndex < ScheduleSyncProtocol.MaxSchedules &&
			   UpdateType >= DetailsUpdateType.NAME && UpdateType <= DetailsUpdateType.ALARM_STATE &&
			   (UpdateType != DetailsUpdateType.NAME ||
			    Name != null && Name.Length <= ScheduleSyncProtocol.MaxScheduleNameLength);
	}
}
