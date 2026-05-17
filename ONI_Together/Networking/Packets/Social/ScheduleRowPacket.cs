using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Social
{
    public class ScheduleRowPacket : IPacket
    {
        // Used for row controls
        public enum RowAction
        {
            SHIFT_UP,
            SHIFT_DOWN,
            ROTATE_LEFT,
            ROTATE_RIGHT,
            DUPLICATE,
            DELETE
        }

        public int ScheduleIndex;
        public RowAction Action;

        // Shifting / Rotating
        public int TimetableToIndex;

        // Duplicating
        public List<ScheduleBlock> NewBlocks = new List<ScheduleBlock>();

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();

            writer.Write(ScheduleIndex);
            writer.Write((int)Action);
            writer.Write(TimetableToIndex);
            switch(Action)
            {
                case RowAction.DUPLICATE:
                    writer.Write(NewBlocks.Count);
                    foreach(ScheduleBlock block in NewBlocks)
                    {
                        writer.Write(block.name);
                        writer.Write(block.GroupId);
                    }
                    break;
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();

            ScheduleIndex = reader.ReadInt32();
            Action = (RowAction)reader.ReadInt32();
            TimetableToIndex = reader.ReadInt32();
            switch(Action)
            {
                case RowAction.DUPLICATE:
                    NewBlocks.Clear();
                    int block_count = reader.ReadInt32();
                    for(int i = 0; i < block_count; i++)
                    {
                        string block_name = reader.ReadString();
                        string group_id = reader.ReadString();
                        ScheduleBlock block = new ScheduleBlock(block_name, group_id);
                        NewBlocks.Add(block);
                    }
                    break;
            }
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (IsApplying)
                return;

            try
            {
                IsApplying = true;
                Apply();
            } finally
            {
                IsApplying = false;
            }
        }

        public void Apply()
        {
            using var _ = Profiler.Scope();

            var schedules = ScheduleManager.Instance.schedules;
            if (schedules == null)
                return;

            Schedule schedule = schedules[ScheduleIndex];
            if (schedule == null)
                return;

            switch(Action)
            {
                case RowAction.SHIFT_UP:
                    ShiftUp(schedule);
                    break;
                case RowAction.SHIFT_DOWN:
                    ShiftDown(schedule);
                    break;
                case RowAction.ROTATE_LEFT:
                    RotateLeft(schedule);
                    break;
                case RowAction.ROTATE_RIGHT:
                    RotateRight(schedule);
                    break;
                case RowAction.DUPLICATE:
                    DuplicateRow(schedule);
                    break;
                case RowAction.DELETE:
                    DeleteRow(schedule);
                    break;
                default:
                    break;
            }
        }

        public void ShiftUp(Schedule schedule)
        {
            using var _ = Profiler.Scope();

            schedule.ShiftTimetable(true, TimetableToIndex);
        }

        public void ShiftDown(Schedule schedule)
        {
            using var _ = Profiler.Scope();

            schedule.ShiftTimetable(false, TimetableToIndex);
        }

        public void RotateLeft(Schedule schedule)
        {
            using var _ = Profiler.Scope();

            schedule.RotateBlocks(true, TimetableToIndex);
        }

        public void RotateRight(Schedule schedule)
        {
            using var _ = Profiler.Scope();

            schedule.RotateBlocks(false, TimetableToIndex);
        }

        public void DuplicateRow(Schedule schedule)
        {
            using var _ = Profiler.Scope();

            // Schedule screen hasn't been opened so we can't update UI
            if(ScheduleScreen.Instance == null)
            {
                List<ScheduleBlock> range = schedule.GetBlocks().GetRange(TimetableToIndex * 24, 24);
                List<ScheduleBlock> list = new List<ScheduleBlock>();
                for (int i = 0; i < range.Count; i++)
                {
                    list.Add(new ScheduleBlock(range[i].name, range[i].GroupId));
                }

                int num = TimetableToIndex + 1;
                schedule.InsertTimetable(num, list);
                return;
            }

            if (ScheduleScreen.Instance.scheduleEntries.Count <= ScheduleIndex)
                return;

            ScheduleScreenEntry entry = ScheduleScreen.Instance.scheduleEntries[ScheduleIndex];
            if (entry == null)
                return;

            entry.DuplicateTimetableRow(TimetableToIndex);
        }

        public void DeleteRow(Schedule schedule)
        {
            using var _ = Profiler.Scope();

            if(ScheduleScreen.Instance == null)
            {
                int index = TimetableToIndex * 24;
                int num = schedule.blocks.Count / 24;
                schedule.blocks.RemoveRange(index, 24);
                bool flag = TimetableToIndex == schedule.progressTimetableIdx;
                bool flag2 = schedule.progressTimetableIdx == num - 1;
                if (TimetableToIndex < schedule.progressTimetableIdx || (flag && flag2))
                {
                    schedule.progressTimetableIdx--;
                }
                // We can't do anything that triggers ScheduleScreen here
                return;
            }

            if (ScheduleScreen.Instance.scheduleEntries.Count <= ScheduleIndex)
                return;

            // TimetableToIndex in this instance is the index of the timetableRows for the assosiated row
            ScheduleScreenEntry entry = ScheduleScreen.Instance.scheduleEntries[ScheduleIndex];
            if(entry != null)
            {
                if (entry.timetableRows.Count == 0 || entry.timetableRows.Count <= TimetableToIndex)
                    return;

                GameObject row = entry.timetableRows[TimetableToIndex];
                if (row != null)
                {
                    entry.RemoveTimetableRow(row);
                }
            }
        }

        public static bool IsApplying = false;

    }
}
