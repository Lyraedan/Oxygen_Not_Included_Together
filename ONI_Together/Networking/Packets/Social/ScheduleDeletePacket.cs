using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using System.IO;

namespace ONI_Together.Networking.Packets.Social
{
	public sealed class ScheduleDeletePacket : IPacket, IClientRelayable
	{
		public ulong ClientRequestId;
		public long BaseRevision;
		public int ScheduleIndex;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid schedule delete request");
			writer.Write(ClientRequestId);
			writer.Write(BaseRevision);
			writer.Write(ScheduleIndex);
		}

		public void Deserialize(BinaryReader reader)
		{
			ClientRequestId = reader.ReadUInt64();
			BaseRevision = reader.ReadInt64();
			ScheduleIndex = reader.ReadInt32();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid schedule delete request");
		}

		public void OnDispatched()
		{
			ScheduleSyncCoordinator.Handle(this);
		}

		internal bool IsWireValid()
			=> ClientRequestId != 0 && BaseRevision >= 0 &&
			   ScheduleIndex >= 0 && ScheduleIndex < ScheduleSyncProtocol.MaxSchedules;
	}
}
