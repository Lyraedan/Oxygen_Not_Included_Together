using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using ONI_MP.Misc;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Social;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Patches.Social
{
    internal class ScheduleScreenEntryPatches
    {
        [HarmonyPatch(typeof(ScheduleScreenEntry), "DuplicateTimetableRow")]
        public static class ScheduleScreenEntry_DuplicateTimetableRow_Postfix
        {
            static void Postfix(ScheduleScreenEntry __instance, int sourceTimetableIdx)
            {
                using var _ = Profiler.Scope();

                if (__instance.IsNullOrDestroyed())
                    return;

                if (__instance.schedule.IsNullOrDestroyed())
                    return;

                if (!MultiplayerSession.InSession)
                    return;
                if (ScheduleRowPacket.IsApplying)
                    return;

                int scheduleIndex = __instance.schedule.GetScheduleIndex();
                if (scheduleIndex == -1)
                    return;

                List<ScheduleBlock> range = __instance.schedule.GetBlocks().GetRange(sourceTimetableIdx * 24, 24);
                List<ScheduleBlock> list = new List<ScheduleBlock>();
                for (int i = 0; i < range.Count; i++)
                {
                    list.Add(new ScheduleBlock(range[i].name, range[i].GroupId));
                }

                ScheduleRowPacket packet = new ScheduleRowPacket()
                {
                    ScheduleIndex = scheduleIndex,
                    Action = ScheduleRowPacket.RowAction.DUPLICATE,
                    TimetableToIndex = sourceTimetableIdx,
                    NewBlocks = list
                };
                PacketSender.SendToAllOtherPeers(packet);
            }
        }

        [HarmonyPatch(typeof(ScheduleScreenEntry), "RemoveTimetableRow")]
        public static class ScheduleScreenEntry_DeleteSchedule_Patch
        {
            private static int rowIndex = -1;

            static bool Prefix(ScheduleScreenEntry __instance, GameObject row)
            {
                using var _ = Profiler.Scope();

                rowIndex = __instance.timetableRows.IndexOf(row); // Cache the row index before deletion
                return true;
            }

            static void Postfix(ScheduleScreenEntry __instance, GameObject row)
            {
                using var _ = Profiler.Scope();

                if (__instance.IsNullOrDestroyed())
                    return;

                if (row.IsNullOrDestroyed())
                    return;

                if (__instance.schedule.IsNullOrDestroyed())
                    return;

                if (!MultiplayerSession.InSession)
                    return;
                if (ScheduleRowPacket.IsApplying)
                    return;

                int scheduleIndex = __instance.schedule.GetScheduleIndex();
                if (scheduleIndex == -1)
                    return;

                // Invalid row index
                if (rowIndex == -1)
                    return;

                ScheduleRowPacket packet = new ScheduleRowPacket()
                {
                    ScheduleIndex = scheduleIndex,
                    Action = ScheduleRowPacket.RowAction.DELETE,
                    TimetableToIndex = rowIndex
                };
                PacketSender.SendToAllOtherPeers(packet);
            }
        }

        [HarmonyPatch(typeof(ScheduleScreenEntry), "OnNameChanged")]
        public static class ScheduleScreenEntry_OnNameChanged_Postfix
        {
            static void Postfix(ScheduleScreenEntry __instance, string newName)
            {
                using var _ = Profiler.Scope();

                if (__instance.IsNullOrDestroyed())
                    return;

                if (__instance.schedule.IsNullOrDestroyed())
                    return;

                if (!MultiplayerSession.InSession)
                    return;
                if (ScheduleDetailsUpdatePacket.IsApplying)
                    return;

                int scheduleIndex = __instance.schedule.GetScheduleIndex();
                if (scheduleIndex == -1)
                    return;

                ScheduleDetailsUpdatePacket packet = new ScheduleDetailsUpdatePacket()
                {
                    ScheduleIndex = scheduleIndex,
                    Name = newName,
                    UpdateType = ScheduleDetailsUpdatePacket.DetailsUpdateType.NAME
                };
                PacketSender.SendToAllOtherPeers(packet);
            }
        }

        [HarmonyPatch(typeof(ScheduleScreenEntry), "OnAlarmClicked")]
        public static class ScheduleScreenEntry_OnAlarmClicked_Postfix
        {
            static void Postfix(ScheduleScreenEntry __instance)
            {
                using var _ = Profiler.Scope();

                if (__instance.IsNullOrDestroyed())
                    return;

                if (__instance.schedule.IsNullOrDestroyed())
                    return;

                if (!MultiplayerSession.InSession)
                    return;
                if (ScheduleDetailsUpdatePacket.IsApplying)
                    return;

                int scheduleIndex = __instance.schedule.GetScheduleIndex();
                if (scheduleIndex == -1)
                    return;

                bool alarmEnabled = __instance.schedule.alarmActivated;
                ScheduleDetailsUpdatePacket packet = new ScheduleDetailsUpdatePacket()
                {
                    ScheduleIndex = scheduleIndex,
                    AlarmActivated = alarmEnabled,
                    UpdateType = ScheduleDetailsUpdatePacket.DetailsUpdateType.ALARM_STATE
                };
                PacketSender.SendToAllOtherPeers(packet);

            }
        }
    }
}
