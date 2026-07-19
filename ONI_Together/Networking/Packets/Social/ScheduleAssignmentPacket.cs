using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using System.IO;

namespace ONI_Together.Networking.Packets.Social
{
	public sealed class ScheduleAssignmentPacket : IPacket, IClientRelayable
	{
		public ulong ClientRequestId;
		public long BaseRevision;
		public int NetId;
		public int ScheduleIndex;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid schedule assignment request");
			writer.Write(ClientRequestId);
			writer.Write(BaseRevision);
			writer.Write(NetId);
			writer.Write(ScheduleIndex);
		}

		public void Deserialize(BinaryReader reader)
		{
			ClientRequestId = reader.ReadUInt64();
			BaseRevision = reader.ReadInt64();
			NetId = reader.ReadInt32();
			ScheduleIndex = reader.ReadInt32();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid schedule assignment request");
		}

		public void OnDispatched()
		{
			ScheduleSyncCoordinator.Handle(this);
		}

		internal bool IsWireValid()
			=> ClientRequestId != 0 && BaseRevision >= 0 && NetId != 0 &&
			   ScheduleIndex >= 0 && ScheduleIndex < ScheduleSyncProtocol.MaxSchedules;
	}
}
