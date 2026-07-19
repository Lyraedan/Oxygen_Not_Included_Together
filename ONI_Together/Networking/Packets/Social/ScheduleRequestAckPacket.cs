using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using System.IO;

namespace ONI_Together.Networking.Packets.Social
{
	public sealed class ScheduleRequestAckPacket : IPacket, IHostOnlyPacket
	{
		public ulong ClientId;
		public ulong ClientRequestId;
		public long HostRevision;
		public bool Accepted;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid schedule request acknowledgement");
			writer.Write(ClientId);
			writer.Write(ClientRequestId);
			writer.Write(HostRevision);
			writer.Write(Accepted);
		}

		public void Deserialize(BinaryReader reader)
		{
			ClientId = reader.ReadUInt64();
			ClientRequestId = reader.ReadUInt64();
			HostRevision = reader.ReadInt64();
			Accepted = reader.ReadBoolean();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid schedule request acknowledgement");
		}

		public void OnDispatched()
		{
			ScheduleSyncCoordinator.HandleAck(this);
		}

		internal bool IsWireValid()
			=> ClientId != 0 && ClientRequestId != 0 && HostRevision >= 0;
	}
}
