using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Social
{
    public class ScheduleBlockUpdatePacket : IPacket
    {

        public int ScheduleIndex;
        public int BlockIndex;
        public string GroupId;

        public static bool IsApplying = false;

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();

            writer.Write(ScheduleIndex);
            writer.Write(BlockIndex);
            writer.Write(GroupId);
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();

            ScheduleIndex = reader.ReadInt32();
            BlockIndex = reader.ReadInt32();
            GroupId = reader.ReadString();
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (IsApplying)
                return;

            Apply();
        }

        private void Apply()
        {
            using var _ = Profiler.Scope();

            List<Schedule> schedules = ScheduleManager.Instance.schedules;
            if (schedules == null) return;

            // Somehow the schedules are out of sync
            while (schedules.Count <= ScheduleIndex)
            {
                var defaultGroups = Db.Get().ScheduleGroups.allGroups;
                ScheduleManager.Instance.AddSchedule(defaultGroups, "Synced Schedule", false);
            }

            var schedule = schedules[ScheduleIndex];

            IsApplying = true;
            try
            {
                var groups = Db.Get().ScheduleGroups;
                var blocks = schedule.blocks;

                var group = groups.resources.Find(g => g.Id == GroupId);
                if(group != null)
                {
                    schedule.SetBlockGroup(BlockIndex, group);
                }

            } finally
            {
                IsApplying = false;
            }

        }

    }
}
