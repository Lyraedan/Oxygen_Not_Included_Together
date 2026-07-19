using ONI_Together.Networking.Components;
using System.Collections.Generic;

namespace ONI_Together.Networking.Packets.Social
{
	internal static partial class ScheduleSyncCoordinator
	{
		private static ScheduleSnapshotPacket CaptureSnapshot(long revision)
		{
			List<Schedule> source = ScheduleManager.Instance?.schedules;
			if (source == null || source.Count == 0 || source.Count > ScheduleSyncProtocol.MaxSchedules)
				return null;
			var packet = new ScheduleSnapshotPacket
			{
				Revision = revision,
				HasDeletedDefaultBionicSchedule = ScheduleManager.Instance.hasDeletedDefaultBionicSchedule,
				ScheduleNameIncrementor = ScheduleManager.Instance.scheduleNameIncrementor
			};
			foreach (Schedule schedule in source)
			{
				ScheduleSnapshotEntry entry = CaptureEntry(schedule);
				if (entry == null)
					return null;
				packet.Schedules.Add(entry);
			}
			return packet.IsWireValid() ? packet : null;
		}

		private static ScheduleSnapshotEntry CaptureEntry(Schedule schedule)
		{
			if (schedule?.blocks == null)
				return null;
			var entry = new ScheduleSnapshotEntry
			{
				Name = schedule.name ?? string.Empty,
				AlarmActivated = schedule.alarmActivated,
				IsDefaultForBionics = schedule.isDefaultForBionics,
				ProgressTimetableIndex = schedule.progressTimetableIdx,
				Tones = (int[])schedule.GetTones().Clone()
			};
			foreach (ScheduleBlock block in schedule.blocks)
				entry.BlockGroupIds.Add(block.GroupId);
			foreach (Ref<Schedulable> reference in schedule.GetAssigned())
			{
				Schedulable assigned = reference.Get();
				int netId = assigned == null ? 0 : assigned.GetNetId();
				if (netId == 0)
					return null;
				entry.AssignedNetIds.Add(netId);
			}
			return entry.IsWireValid() ? entry : null;
		}

		private static bool TryBuildSnapshotState(
			ScheduleSnapshotPacket packet,
			out List<Schedule> schedules,
			out List<List<Schedulable>> assignments)
		{
			schedules = new List<Schedule>(packet.Schedules.Count);
			assignments = new List<List<Schedulable>>(packet.Schedules.Count);
			UnityEngine.Random.State randomState = UnityEngine.Random.state;
			try
			{
				foreach (ScheduleSnapshotEntry entry in packet.Schedules)
				{
					if (!TryCreateSchedule(entry, out Schedule schedule) ||
					    !TryResolveAssignments(entry, out List<Schedulable> resolved))
						return false;
					schedules.Add(schedule);
					assignments.Add(resolved);
				}
				return true;
			}
			finally
			{
				UnityEngine.Random.state = randomState;
			}
		}

		private static bool TryCreateSchedule(ScheduleSnapshotEntry entry, out Schedule schedule)
		{
			schedule = null;
			var blocks = new List<ScheduleBlock>(entry.BlockGroupIds.Count);
			foreach (string groupId in entry.BlockGroupIds)
			{
				ScheduleGroup group = Db.Get().ScheduleGroups.resources.Find(item => item.Id == groupId);
				if (group == null)
					return false;
				blocks.Add(new ScheduleBlock(group.Name, group.Id));
			}
			schedule = new Schedule(entry.Name, blocks, entry.AlarmActivated)
			{
				isDefaultForBionics = entry.IsDefaultForBionics,
				progressTimetableIdx = entry.ProgressTimetableIndex,
				tones = (int[])entry.Tones.Clone()
			};
			return true;
		}

		private static bool TryResolveAssignments(
			ScheduleSnapshotEntry entry,
			out List<Schedulable> assignments)
		{
			assignments = new List<Schedulable>(entry.AssignedNetIds.Count);
			foreach (int netId in entry.AssignedNetIds)
			{
				if (!NetworkIdentityRegistry.TryGet(netId, out NetworkIdentity identity))
					return false;
				Schedulable schedulable = identity.GetComponent<Schedulable>();
				if (schedulable == null)
					return false;
				assignments.Add(schedulable);
			}
			return true;
		}

		private static void AssignSnapshotEntities(
			IReadOnlyList<Schedule> schedules,
			IReadOnlyList<List<Schedulable>> assignments)
		{
			for (int i = 0; i < schedules.Count; i++)
				foreach (Schedulable schedulable in assignments[i])
					schedules[i].Assign(schedulable);
		}

		private static bool TryGetSchedule(int index, out Schedule schedule)
		{
			List<Schedule> schedules = ScheduleManager.Instance?.schedules;
			if (schedules == null || index < 0 || index >= schedules.Count)
			{
				schedule = null;
				return false;
			}
			schedule = schedules[index];
			return schedule != null;
		}

		private static long GetTotalBlockCount()
		{
			long total = 0;
			List<Schedule> schedules = ScheduleManager.Instance?.schedules;
			if (schedules == null)
				return int.MaxValue;
			foreach (Schedule schedule in schedules)
			{
				total += schedule?.blocks?.Count ?? 0;
				if (total > ScheduleSyncProtocol.MaxTotalBlocks)
					return total;
			}
			return total;
		}
	}
}
