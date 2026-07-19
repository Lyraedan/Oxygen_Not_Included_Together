using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using System.IO;

namespace ONI_Together.Networking.Packets.Social
{
	public sealed class ScheduleRowPacket : IPacket, IClientRelayable
	{
		public enum RowAction : byte
		{
			SHIFT_UP,
			SHIFT_DOWN,
			ROTATE_LEFT,
			ROTATE_RIGHT,
			DUPLICATE,
			DELETE,
			RESET_DEFAULT
		}

		public ulong ClientRequestId;
		public long BaseRevision;
		public int ScheduleIndex;
		public RowAction Action;
		public int TimetableToIndex;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid schedule row request");
			writer.Write(ClientRequestId);
			writer.Write(BaseRevision);
			writer.Write(ScheduleIndex);
			writer.Write((byte)Action);
			writer.Write(TimetableToIndex);
		}

		public void Deserialize(BinaryReader reader)
		{
			ClientRequestId = reader.ReadUInt64();
			BaseRevision = reader.ReadInt64();
			ScheduleIndex = reader.ReadInt32();
			Action = (RowAction)reader.ReadByte();
			TimetableToIndex = reader.ReadInt32();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid schedule row request");
		}

		public void OnDispatched()
		{
			ScheduleSyncCoordinator.Handle(this);
		}

		internal bool IsWireValid()
			=> ClientRequestId != 0 && BaseRevision >= 0 &&
			   ScheduleIndex >= 0 && ScheduleIndex < ScheduleSyncProtocol.MaxSchedules &&
			   Action >= RowAction.SHIFT_UP && Action <= RowAction.RESET_DEFAULT &&
			   (Action == RowAction.RESET_DEFAULT
				   ? TimetableToIndex == 0
				   : TimetableToIndex >= 0 &&
				     TimetableToIndex < ScheduleSyncProtocol.MaxBlocksPerSchedule /
				     ScheduleSyncProtocol.BlocksPerTimetable);
	}
}
