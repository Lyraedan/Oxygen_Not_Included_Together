using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using System.IO;

namespace ONI_Together.Networking.Packets.Social
{
	public sealed class ScheduleBlockUpdatePacket : IPacket, IClientRelayable
	{
		public ulong ClientRequestId;
		public long BaseRevision;
		public int ScheduleIndex;
		public int BlockIndex;
		public string GroupId = string.Empty;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid schedule block request");
			writer.Write(ClientRequestId);
			writer.Write(BaseRevision);
			writer.Write(ScheduleIndex);
			writer.Write(BlockIndex);
			ScheduleSyncProtocol.WriteString(writer, GroupId, ScheduleSyncProtocol.MaxGroupIdLength);
		}

		public void Deserialize(BinaryReader reader)
		{
			ClientRequestId = reader.ReadUInt64();
			BaseRevision = reader.ReadInt64();
			ScheduleIndex = reader.ReadInt32();
			BlockIndex = reader.ReadInt32();
			GroupId = ScheduleSyncProtocol.ReadString(reader, ScheduleSyncProtocol.MaxGroupIdLength);
			if (!IsWireValid())
				throw new InvalidDataException("Invalid schedule block request");
		}

		public void OnDispatched()
		{
			ScheduleSyncCoordinator.Handle(this);
		}

		internal bool IsWireValid()
			=> ClientRequestId != 0 && BaseRevision >= 0 &&
			   ScheduleIndex >= 0 && ScheduleIndex < ScheduleSyncProtocol.MaxSchedules &&
			   BlockIndex >= 0 && BlockIndex < ScheduleSyncProtocol.MaxBlocksPerSchedule &&
			   !string.IsNullOrEmpty(GroupId) && GroupId.Length <= ScheduleSyncProtocol.MaxGroupIdLength;
	}
}
