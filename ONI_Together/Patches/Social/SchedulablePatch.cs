using HarmonyLib;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Social;
using Shared.Profiling;

namespace ONI_Together.Patches.Social
{
	[HarmonyPatch(typeof(Schedule), nameof(Schedule.Assign))]
	public static class ScheduleAssignPatch
	{
		public static void Postfix()
		{
			using var _ = Profiler.Scope();
			ScheduleSyncCoordinator.PublishHostMutation();
		}
	}

	[HarmonyPatch(typeof(ScheduleMinionWidget), nameof(ScheduleMinionWidget.ChangeAssignment))]
	public static class ScheduleChangeAssignmentPatch
	{
		public static bool Prefix(Schedule targetSchedule, Schedulable schedulable)
		{
			using var _ = Profiler.Scope();
			if (!MultiplayerSession.InSession || MultiplayerSession.IsHost ||
			    ScheduleSyncCoordinator.IsApplyingAuthoritativeMutation)
				return true;
			if (targetSchedule == null || schedulable == null || targetSchedule.IsAssigned(schedulable))
				return false;

			int scheduleIndex = targetSchedule.GetScheduleIndex();
			int netId = schedulable.GetNetId();
			if (scheduleIndex < 0 || netId == 0)
				return false;
			ScheduleSyncCoordinator.SendClientRequest(new ScheduleAssignmentPacket
			{
				NetId = netId,
				ScheduleIndex = scheduleIndex
			});
			return false;
		}
	}

	[HarmonyPatch(typeof(Schedule), nameof(Schedule.ShiftTimetable))]
	public static class ScheduleShiftTimetablePatch
	{
		public static bool Prefix(
			Schedule __instance,
			bool up,
			int timetableToShiftIdx,
			ref bool __result)
		{
			using var _ = Profiler.Scope();
			if (!MultiplayerSession.InSession || MultiplayerSession.IsHost ||
			    ScheduleSyncCoordinator.IsApplyingAuthoritativeMutation)
				return true;

			int rowCount = __instance?.blocks?.Count / ScheduleSyncProtocol.BlocksPerTimetable ?? 0;
			int scheduleIndex = __instance?.GetScheduleIndex() ?? -1;
			bool valid = scheduleIndex >= 0 && timetableToShiftIdx >= 0 &&
			             timetableToShiftIdx < rowCount &&
			             !(up && timetableToShiftIdx == 0) &&
			             !(!up && timetableToShiftIdx == rowCount - 1);
			if (valid)
			{
				ScheduleSyncCoordinator.SendClientRequest(new ScheduleRowPacket
				{
					ScheduleIndex = scheduleIndex,
					Action = up
						? ScheduleRowPacket.RowAction.SHIFT_UP
						: ScheduleRowPacket.RowAction.SHIFT_DOWN,
					TimetableToIndex = timetableToShiftIdx
				});
			}
			__result = valid;
			return false;
		}

		public static void Postfix(bool __result)
		{
			if (__result)
				ScheduleSyncCoordinator.PublishHostMutation();
		}
	}

	[HarmonyPatch(typeof(Schedule), nameof(Schedule.RotateBlocks))]
	public static class ScheduleRotateBlocksPatch
	{
		public static bool Prefix(Schedule __instance, bool directionLeft, int timetableToRotateIdx)
		{
			using var _ = Profiler.Scope();
			if (!MultiplayerSession.InSession || MultiplayerSession.IsHost ||
			    ScheduleSyncCoordinator.IsApplyingAuthoritativeMutation)
				return true;

			int rowCount = __instance?.blocks?.Count / ScheduleSyncProtocol.BlocksPerTimetable ?? 0;
			int scheduleIndex = __instance?.GetScheduleIndex() ?? -1;
			if (scheduleIndex < 0 || timetableToRotateIdx < 0 || timetableToRotateIdx >= rowCount)
				return false;
			ScheduleSyncCoordinator.SendClientRequest(new ScheduleRowPacket
			{
				ScheduleIndex = scheduleIndex,
				Action = directionLeft
					? ScheduleRowPacket.RowAction.ROTATE_LEFT
					: ScheduleRowPacket.RowAction.ROTATE_RIGHT,
				TimetableToIndex = timetableToRotateIdx
			});
			return false;
		}

		public static void Postfix()
		{
			ScheduleSyncCoordinator.PublishHostMutation();
		}
	}
}
