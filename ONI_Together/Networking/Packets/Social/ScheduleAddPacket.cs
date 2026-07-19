using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using System.IO;

namespace ONI_Together.Networking.Packets.Social
{
	public sealed class ScheduleAddPacket : IPacket, IClientRelayable
	{
		public ulong ClientRequestId;
		public long BaseRevision;
		public bool Duplicated;
		public int SourceScheduleIndex = -1;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid schedule add request");
			writer.Write(ClientRequestId);
			writer.Write(BaseRevision);
			writer.Write(Duplicated);
			writer.Write(SourceScheduleIndex);
		}

		public void Deserialize(BinaryReader reader)
		{
			ClientRequestId = reader.ReadUInt64();
			BaseRevision = reader.ReadInt64();
			Duplicated = reader.ReadBoolean();
			SourceScheduleIndex = reader.ReadInt32();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid schedule add request");
		}

		public void OnDispatched()
		{
			ScheduleSyncCoordinator.Handle(this);
		}

		internal bool IsWireValid()
			=> ClientRequestId != 0 && BaseRevision >= 0 &&
			   (Duplicated
				   ? SourceScheduleIndex >= 0 && SourceScheduleIndex < ScheduleSyncProtocol.MaxSchedules
				   : SourceScheduleIndex == -1);
	}
}
