using HarmonyLib;
using KSerialization;
using ONI_MP.Misc;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Social;
using System.Collections.Generic;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.Social
{
	// Sync assignments (Minion -> Schedule)
	[HarmonyPatch(typeof(Schedule), "Assign")]
	public static class ScheduleAssignPatch
	{
		public static void Postfix(Schedule __instance, Schedulable schedulable)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession) return;
			if (ScheduleAssignmentPacket.IsApplying) return;

			int netId = schedulable.GetNetId();
			if (netId != 0)
			{
				int index = __instance.GetScheduleIndex();
				if (index != -1)
				{
					var packet = new ScheduleAssignmentPacket
					{
						NetId = netId,
						ScheduleIndex = index
					};

					PacketSender.SendToAllOtherPeers(packet);
				}
			}
		}
	}

	[HarmonyPatch(typeof(Schedule), "ShiftTimetable")]
	public static class Schedule_ShiftTimetable_Patch
	{
		static void Postfix(Schedule __instance, bool up, int timetableToShiftIdx, bool __result)
		{
			using var _ = Profiler.Scope();

			if (!__result)
				return;

			if (!MultiplayerSession.InSession)
				return;

			if (ScheduleRowPacket.IsApplying)
				return;

			int scheduleIndex = __instance.GetScheduleIndex();
			if (scheduleIndex == -1)
				return;

			ScheduleRowPacket packet = new ScheduleRowPacket()
			{
				ScheduleIndex = scheduleIndex,
				Action = up ? ScheduleRowPacket.RowAction.SHIFT_UP : ScheduleRowPacket.RowAction.SHIFT_DOWN,
				TimetableToIndex = timetableToShiftIdx
			};
			PacketSender.SendToAllOtherPeers(packet);
		}
	}

    [HarmonyPatch(typeof(Schedule), "RotateBlocks")]
    public static class Schedule_RotateBlocks_Patch
    {
        static void Postfix(Schedule __instance, bool directionLeft, int timetableToRotateIdx)
        {
	        using var _ = Profiler.Scope();

            if (!MultiplayerSession.InSession)
                return;

            if (ScheduleRowPacket.IsApplying)
                return;

            int scheduleIndex = __instance.GetScheduleIndex();
            if (scheduleIndex == -1)
                return;

            ScheduleRowPacket packet = new ScheduleRowPacket()
            {
                ScheduleIndex = scheduleIndex,
                Action = directionLeft ? ScheduleRowPacket.RowAction.ROTATE_LEFT : ScheduleRowPacket.RowAction.ROTATE_RIGHT,
                TimetableToIndex = timetableToRotateIdx
            };
            PacketSender.SendToAllOtherPeers(packet);
        }
    }
}
