using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using System;
using System.Collections.Generic;

namespace ONI_Together.Networking.Packets.Social
{
	internal static partial class ScheduleSyncCoordinator
	{
		private static ScheduleManager _trackedManager;
		private static long _hostRevision;
		private static long _appliedRevision;
		private static bool _applyingHostRequest;
		private static bool _applyingSnapshot;
		private static readonly Queue<IPacket> PendingClientRequests = new();
		private static ulong _nextClientRequestId;
		private static ulong _inFlightClientRequestId;
		private static IPacket _inFlightClientPacket;
		private static ScheduleRequestAckPacket _deferredAck;
		private static ulong _trackedHostId;
		private static long _trackedSnapshotGeneration;
		private static int _hostMutationBatchDepth;
		private static bool _hostMutationBatchDirty;

		internal static bool IsApplyingAuthoritativeMutation => _applyingHostRequest || _applyingSnapshot;

		internal static bool CanAddSchedule()
			=> ScheduleManager.Instance?.schedules != null &&
			   ScheduleManager.Instance.schedules.Count < ScheduleSyncProtocol.MaxSchedules &&
			   GetTotalBlockCount() + ScheduleSyncProtocol.BlocksPerTimetable <=
			   ScheduleSyncProtocol.MaxTotalBlocks;

		internal static bool CanDuplicateRow(Schedule schedule)
			=> schedule?.blocks != null &&
			   schedule.blocks.Count + ScheduleSyncProtocol.BlocksPerTimetable <=
			   ScheduleSyncProtocol.MaxBlocksPerSchedule &&
			   GetTotalBlockCount() + ScheduleSyncProtocol.BlocksPerTimetable <=
			   ScheduleSyncProtocol.MaxTotalBlocks;

		internal static bool CanDuplicateSchedule(Schedule schedule)
			=> schedule?.blocks != null &&
			   ScheduleManager.Instance?.schedules != null &&
			   ScheduleManager.Instance.schedules.Count < ScheduleSyncProtocol.MaxSchedules &&
			   schedule.blocks.Count <= ScheduleSyncProtocol.MaxBlocksPerSchedule &&
			   GetTotalBlockCount() + schedule.blocks.Count <= ScheduleSyncProtocol.MaxTotalBlocks;

		internal static bool IsValidName(string name)
			=> name != null && name.Length <= ScheduleSyncProtocol.MaxScheduleNameLength;

		internal static void SendClientRequest(IPacket packet)
		{
			if (!MultiplayerSession.IsClient || IsApplyingAuthoritativeMutation ||
			    !TrackCurrentManager())
				return;
			PendingClientRequests.Enqueue(packet);
			TrySendNextClientRequest();
		}

		internal static void Handle(ScheduleBlockUpdatePacket packet)
			=> RunHostRequest(packet.ClientRequestId, packet.BaseRevision,
				() => ApplyBlock(packet), nameof(ScheduleBlockUpdatePacket));

		internal static void Handle(ScheduleAddPacket packet)
			=> RunHostRequest(packet.ClientRequestId, packet.BaseRevision,
				() => ApplyAdd(packet), nameof(ScheduleAddPacket));

		internal static void Handle(ScheduleDeletePacket packet)
			=> RunHostRequest(packet.ClientRequestId, packet.BaseRevision,
				() => ApplyDelete(packet), nameof(ScheduleDeletePacket));

		internal static void Handle(ScheduleDetailsUpdatePacket packet)
			=> RunHostRequest(packet.ClientRequestId, packet.BaseRevision,
				() => ApplyDetails(packet), nameof(ScheduleDetailsUpdatePacket));

		internal static void Handle(ScheduleRowPacket packet)
			=> RunHostRequest(packet.ClientRequestId, packet.BaseRevision,
				() => ApplyRow(packet), nameof(ScheduleRowPacket));

		internal static void Handle(ScheduleAssignmentPacket packet)
			=> RunHostRequest(packet.ClientRequestId, packet.BaseRevision,
				() => ApplyAssignment(packet), nameof(ScheduleAssignmentPacket));

		internal static void HandleAck(ScheduleRequestAckPacket packet)
		{
			DispatchContext context = PacketHandler.CurrentContext;
			if (!MultiplayerSession.IsClient || !context.SenderIsHost ||
			    packet.ClientId != MultiplayerSession.LocalUserID ||
			    packet.ClientRequestId != _inFlightClientRequestId)
				return;
			if (packet.HostRevision > _appliedRevision)
			{
				_deferredAck = packet;
				return;
			}
			CompleteClientRequest(packet.Accepted);
		}

		internal static void PublishHostMutation()
		{
			if (!MultiplayerSession.IsHostInSession || IsApplyingAuthoritativeMutation || !TrackCurrentManager())
				return;
			if (_hostMutationBatchDepth > 0)
			{
				_hostMutationBatchDirty = true;
				return;
			}
			PublishNextRevision();
		}

		internal static void ApplySnapshot(ScheduleSnapshotPacket packet)
		{
			DispatchContext context = PacketHandler.CurrentContext;
			if (!MultiplayerSession.IsClient || !context.SenderIsHost || !TrackCurrentManager() ||
			    !ScheduleSyncProtocol.ShouldApplySnapshot(packet.Revision, _appliedRevision))
				return;
			if (!TryBuildSnapshotState(packet, out List<Schedule> schedules,
			    out List<List<Schedulable>> assignments))
				return;

			_applyingSnapshot = true;
			try
			{
				ScheduleManager.Instance.schedules = schedules;
				ScheduleManager.Instance.hasDeletedDefaultBionicSchedule =
					packet.HasDeletedDefaultBionicSchedule;
				ScheduleManager.Instance.scheduleNameIncrementor = packet.ScheduleNameIncrementor;
				AssignSnapshotEntities(schedules, assignments);
				_appliedRevision = packet.Revision;
				RefreshScheduleScreen();
				TryCompleteDeferredAck();
			}
			finally
			{
				_applyingSnapshot = false;
			}
		}

		private static void RunHostRequest(
			ulong requestId,
			long baseRevision,
			Func<bool> mutation,
			string requestName)
		{
			DispatchContext context = PacketHandler.CurrentContext;
			if (!MultiplayerSession.IsHost || context.SenderIsHost || !TrackCurrentManager())
				return;
			if (!ScheduleSyncProtocol.IsCurrentRevision(baseRevision, _hostRevision))
			{
				DebugConsole.LogWarning($"[{requestName}] Rejected stale revision {baseRevision}, host={_hostRevision}");
				SendCurrentSnapshot(context.SenderId);
				SendAck(context.SenderId, requestId, accepted: false);
				return;
			}
			long currentRevision = _hostRevision > 0 ? _hostRevision : 1;
			if (CaptureSnapshot(currentRevision) == null)
			{
				DebugConsole.LogWarning($"[{requestName}] Host schedule state is not snapshot-safe");
				SendAck(context.SenderId, requestId, accepted: false);
				return;
			}

			bool changed = false;
			_applyingHostRequest = true;
			try
			{
				changed = mutation();
			}
			catch (Exception exception)
			{
				DebugConsole.LogWarning($"[{requestName}] Host mutation failed: {exception}");
			}
			finally
			{
				_applyingHostRequest = false;
			}
			if (!changed)
			{
				SendCurrentSnapshot(context.SenderId);
				SendAck(context.SenderId, requestId, accepted: false);
				return;
			}
			RefreshScheduleScreen();
			bool accepted = PublishNextRevision();
			SendAck(context.SenderId, requestId, accepted);
		}

		private static bool ApplyBlock(ScheduleBlockUpdatePacket packet)
		{
			if (!TryGetSchedule(packet.ScheduleIndex, out Schedule schedule) ||
			    packet.BlockIndex >= schedule.blocks.Count)
				return false;
			ScheduleGroup group = Db.Get().ScheduleGroups.resources.Find(item => item.Id == packet.GroupId);
			if (group == null || schedule.blocks[packet.BlockIndex].GroupId == packet.GroupId)
				return false;
			schedule.SetBlockGroup(packet.BlockIndex, group);
			return true;
		}

		private static bool ApplyAdd(ScheduleAddPacket packet)
		{
			ScheduleManager manager = ScheduleManager.Instance;
			if (!packet.Duplicated)
			{
				if (!CanAddSchedule())
					return false;
				manager.AddSchedule(
					Db.Get().ScheduleGroups.allGroups,
					global::STRINGS.UI.SCHEDULESCREEN.SCHEDULE_NAME_NEW,
					alarmOn: false);
				return true;
			}
			if (!TryGetSchedule(packet.SourceScheduleIndex, out Schedule source) ||
			    !CanDuplicateSchedule(source))
				return false;
			manager.DuplicateSchedule(source);
			return true;
		}

		private static bool ApplyDelete(ScheduleDeletePacket packet)
		{
			if (ScheduleManager.Instance.schedules.Count <= 1 ||
			    !TryGetSchedule(packet.ScheduleIndex, out Schedule schedule))
				return false;
			ScheduleManager.Instance.DeleteSchedule(schedule);
			return true;
		}

		private static bool ApplyDetails(ScheduleDetailsUpdatePacket packet)
		{
			if (!TryGetSchedule(packet.ScheduleIndex, out Schedule schedule))
				return false;
			if (packet.UpdateType == ScheduleDetailsUpdatePacket.DetailsUpdateType.NAME)
			{
				if (schedule.name == packet.Name)
					return false;
				schedule.name = packet.Name;
				return true;
			}
			if (schedule.alarmActivated == packet.AlarmActivated)
				return false;
			schedule.alarmActivated = packet.AlarmActivated;
			return true;
		}

		private static bool ApplyRow(ScheduleRowPacket packet)
		{
			if (!TryGetSchedule(packet.ScheduleIndex, out Schedule schedule))
				return false;
			int rowCount = schedule.blocks.Count / ScheduleSyncProtocol.BlocksPerTimetable;
			if (packet.TimetableToIndex < 0 || packet.TimetableToIndex >= rowCount)
				return false;

			return packet.Action switch
			{
				ScheduleRowPacket.RowAction.SHIFT_UP => schedule.ShiftTimetable(true, packet.TimetableToIndex),
				ScheduleRowPacket.RowAction.SHIFT_DOWN => schedule.ShiftTimetable(false, packet.TimetableToIndex),
				ScheduleRowPacket.RowAction.ROTATE_LEFT => Rotate(schedule, true, packet.TimetableToIndex),
				ScheduleRowPacket.RowAction.ROTATE_RIGHT => Rotate(schedule, false, packet.TimetableToIndex),
				ScheduleRowPacket.RowAction.DUPLICATE => DuplicateRow(schedule, packet.TimetableToIndex),
				ScheduleRowPacket.RowAction.DELETE => DeleteRow(schedule, packet.TimetableToIndex),
				ScheduleRowPacket.RowAction.RESET_DEFAULT => ResetToDefaults(schedule),
				_ => false
			};
		}

		private static bool ResetToDefaults(Schedule schedule)
		{
			List<ScheduleBlock> defaults = Schedule.GetScheduleBlocksFromGroupDefaults(
				Db.Get().ScheduleGroups.allGroups);
			if (schedule.blocks.Count == defaults.Count)
			{
				bool matches = true;
				for (int i = 0; i < defaults.Count; i++)
					matches &= schedule.blocks[i].GroupId == defaults[i].GroupId;
				if (matches)
					return false;
			}
			schedule.SetBlocksToGroupDefaults(Db.Get().ScheduleGroups.allGroups);
			return true;
		}

		private static bool Rotate(Schedule schedule, bool left, int rowIndex)
		{
			schedule.RotateBlocks(left, rowIndex);
			return true;
		}

		private static bool DuplicateRow(Schedule schedule, int rowIndex)
		{
			if (!CanDuplicateRow(schedule))
				return false;
			int start = rowIndex * ScheduleSyncProtocol.BlocksPerTimetable;
			var blocks = new List<ScheduleBlock>(ScheduleSyncProtocol.BlocksPerTimetable);
			for (int i = 0; i < ScheduleSyncProtocol.BlocksPerTimetable; i++)
			{
				ScheduleBlock source = schedule.blocks[start + i];
				blocks.Add(new ScheduleBlock(source.name, source.GroupId));
			}
			schedule.InsertTimetable(rowIndex + 1, blocks);
			schedule.Changed();
			return true;
		}

		private static bool DeleteRow(Schedule schedule, int rowIndex)
		{
			int rowCount = schedule.blocks.Count / ScheduleSyncProtocol.BlocksPerTimetable;
			if (rowCount <= 1)
				return false;
			schedule.blocks.RemoveRange(rowIndex * ScheduleSyncProtocol.BlocksPerTimetable,
				ScheduleSyncProtocol.BlocksPerTimetable);
			bool removingCurrent = rowIndex == schedule.progressTimetableIdx;
			if (rowIndex < schedule.progressTimetableIdx || removingCurrent && rowIndex == rowCount - 1)
				schedule.progressTimetableIdx--;
			schedule.Changed();
			return true;
		}

		private static bool ApplyAssignment(ScheduleAssignmentPacket packet)
		{
			if (!TryGetSchedule(packet.ScheduleIndex, out Schedule target) ||
			    !NetworkIdentityRegistry.TryGet(packet.NetId, out NetworkIdentity identity))
				return false;
			Schedulable schedulable = identity.GetComponent<Schedulable>();
			if (schedulable == null || target.IsAssigned(schedulable))
				return false;
			ScheduleManager.Instance.GetSchedule(schedulable)?.Unassign(schedulable);
			target.Assign(schedulable);
			return true;
		}

		private static bool PublishNextRevision()
		{
			if (_hostRevision == long.MaxValue)
			{
				DebugConsole.LogError("[ScheduleSync] Revision exhausted", false);
				return false;
			}
			long next = _hostRevision + 1;
			ScheduleSnapshotPacket snapshot = CaptureSnapshot(next);
			if (snapshot == null)
				return false;
			_hostRevision = next;
			PacketSender.SendToAllClients(snapshot, PacketSendMode.ReliableImmediate);
			return true;
		}

		private static void SendCurrentSnapshot(ulong playerId)
		{
			long revision = _hostRevision > 0 ? _hostRevision : 1;
			ScheduleSnapshotPacket snapshot = CaptureSnapshot(revision);
			if (snapshot == null)
				return;
			_hostRevision = revision;
			PacketSender.SendToPlayer(playerId, snapshot, PacketSendMode.ReliableImmediate);
		}

		private static void SendAck(ulong playerId, ulong requestId, bool accepted)
		{
			PacketSender.SendToPlayer(playerId, new ScheduleRequestAckPacket
			{
				ClientId = playerId,
				ClientRequestId = requestId,
				HostRevision = _hostRevision,
				Accepted = accepted
			}, PacketSendMode.ReliableImmediate);
		}

		private static bool TrackCurrentManager()
		{
			ScheduleManager manager = ScheduleManager.Instance;
			if (manager == null)
				return false;
			ulong hostId = MultiplayerSession.InSession ? MultiplayerSession.HostUserID : 0;
			long snapshotGeneration = MultiplayerSession.IsClient
				? ReadyManager.ClientSnapshotGeneration
				: 0;
			if (ReferenceEquals(manager, _trackedManager) && hostId == _trackedHostId &&
			    snapshotGeneration == _trackedSnapshotGeneration)
				return true;
			_trackedManager = manager;
			_trackedHostId = hostId;
			_trackedSnapshotGeneration = snapshotGeneration;
			_hostRevision = 0;
			_appliedRevision = 0;
			PendingClientRequests.Clear();
			_inFlightClientRequestId = 0;
			_inFlightClientPacket = null;
			_deferredAck = null;
			_hostMutationBatchDepth = 0;
			_hostMutationBatchDirty = false;
			return true;
		}

		private static void RefreshScheduleScreen()
		{
			ScheduleScreen.Instance?.OnSchedulesChanged(ScheduleManager.Instance.schedules);
		}
	}
}
