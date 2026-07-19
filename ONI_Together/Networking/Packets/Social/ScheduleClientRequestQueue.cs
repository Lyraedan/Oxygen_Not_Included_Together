using ONI_Together.Networking.Packets.Architecture;

namespace ONI_Together.Networking.Packets.Social
{
	internal static partial class ScheduleSyncCoordinator
	{
		private static void TrySendNextClientRequest()
		{
			if (_inFlightClientRequestId != 0 || PendingClientRequests.Count == 0 ||
			    !TrackCurrentManager())
				return;
			IPacket packet = PendingClientRequests.Dequeue();
			ulong requestId = NextClientRequestId();
			if (!StampClientRequest(packet, requestId, _appliedRevision))
				return;
			_inFlightClientRequestId = requestId;
			_inFlightClientPacket = packet;
			PacketSender.SendToAllOtherPeers(packet);
		}

		private static ulong NextClientRequestId()
		{
			_nextClientRequestId++;
			if (_nextClientRequestId == 0)
				_nextClientRequestId++;
			return _nextClientRequestId;
		}

		private static bool StampClientRequest(IPacket packet, ulong requestId, long revision)
		{
			switch (packet)
			{
				case ScheduleBlockUpdatePacket value:
					value.ClientRequestId = requestId; value.BaseRevision = revision; return true;
				case ScheduleAddPacket value:
					value.ClientRequestId = requestId; value.BaseRevision = revision; return true;
				case ScheduleDeletePacket value:
					value.ClientRequestId = requestId; value.BaseRevision = revision; return true;
				case ScheduleDetailsUpdatePacket value:
					value.ClientRequestId = requestId; value.BaseRevision = revision; return true;
				case ScheduleRowPacket value:
					value.ClientRequestId = requestId; value.BaseRevision = revision; return true;
				case ScheduleAssignmentPacket value:
					value.ClientRequestId = requestId; value.BaseRevision = revision; return true;
				default:
					return false;
			}
		}

		private static void TryCompleteDeferredAck()
		{
			if (_deferredAck == null || _deferredAck.HostRevision > _appliedRevision ||
			    _deferredAck.ClientRequestId != _inFlightClientRequestId)
				return;
			CompleteClientRequest(_deferredAck.Accepted);
		}

		private static void CompleteClientRequest(bool accepted)
		{
			if (accepted && _inFlightClientPacket is ScheduleDeletePacket deleted)
				RebasePendingRequestsAfterDelete(deleted.ScheduleIndex);
			else if (!accepted)
				PendingClientRequests.Clear();
			_inFlightClientRequestId = 0;
			_inFlightClientPacket = null;
			_deferredAck = null;
			TrySendNextClientRequest();
		}

		private static void RebasePendingRequestsAfterDelete(int deletedIndex)
		{
			int count = PendingClientRequests.Count;
			for (int i = 0; i < count; i++)
			{
				IPacket pending = PendingClientRequests.Dequeue();
				if (TryRebaseScheduleIndex(pending, deletedIndex))
					PendingClientRequests.Enqueue(pending);
			}
		}

		private static bool TryRebaseScheduleIndex(IPacket packet, int deletedIndex)
		{
			int index;
			switch (packet)
			{
				case ScheduleBlockUpdatePacket value: index = value.ScheduleIndex; break;
				case ScheduleDeletePacket value: index = value.ScheduleIndex; break;
				case ScheduleDetailsUpdatePacket value: index = value.ScheduleIndex; break;
				case ScheduleRowPacket value: index = value.ScheduleIndex; break;
				case ScheduleAssignmentPacket value: index = value.ScheduleIndex; break;
				case ScheduleAddPacket value when value.Duplicated: index = value.SourceScheduleIndex; break;
				default: return true;
			}
			if (!ScheduleSyncProtocol.TryRebaseScheduleIndex(
				index, deletedIndex, out int rebasedIndex))
				return false;
			if (rebasedIndex == index)
				return true;
			switch (packet)
			{
				case ScheduleBlockUpdatePacket value: value.ScheduleIndex = rebasedIndex; break;
				case ScheduleDeletePacket value: value.ScheduleIndex = rebasedIndex; break;
				case ScheduleDetailsUpdatePacket value: value.ScheduleIndex = rebasedIndex; break;
				case ScheduleRowPacket value: value.ScheduleIndex = rebasedIndex; break;
				case ScheduleAssignmentPacket value: value.ScheduleIndex = rebasedIndex; break;
				case ScheduleAddPacket value: value.SourceScheduleIndex = rebasedIndex; break;
			}
			return true;
		}
	}
}
