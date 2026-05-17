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
    public class ScheduleAddPacket : IPacket
    {
        public string Name;
        public List<ScheduleBlock> Blocks = new List<ScheduleBlock>();
        public bool AlarmActivated;
        public bool Duplicated;

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();

            writer.Write(Name);
            writer.Write(Blocks.Count);
            foreach (ScheduleBlock block in Blocks)
            {
                writer.Write(block.name);
                writer.Write(block.GroupId);
            }
            writer.Write(AlarmActivated);
            writer.Write(Duplicated);
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();

            Name = reader.ReadString();
            Blocks.Clear();
            int blocks_count = reader.ReadInt32();
            for(int i = 0; i < blocks_count; i++)
            {
                string blockName = reader.ReadString();
                string groupId = reader.ReadString();
                ScheduleBlock block = new ScheduleBlock(blockName, groupId);
                Blocks.Add(block);
            }
            AlarmActivated = reader.ReadBoolean();
            Duplicated = reader.ReadBoolean();
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

            try
            {
                IsApplying = true;
                if (Duplicated)
                {
                    DuplicateSchedule();
                }
                else
                {
                    AddNewSchedule();
                }
            } finally
            {
                IsApplying = false;
            }
        }

        private void AddNewSchedule()
        {
            using var _ = Profiler.Scope();

            ScheduleManager.Instance.AddSchedule(Db.Get().ScheduleGroups.allGroups, Name, AlarmActivated);
        }

        private void DuplicateSchedule()
        {
            using var _ = Profiler.Scope();

            Schedule source = new Schedule(Name, Blocks, AlarmActivated);
            ScheduleManager.Instance.DuplicateSchedule(source);
        }

        public static bool IsApplying = false;
    }
}
