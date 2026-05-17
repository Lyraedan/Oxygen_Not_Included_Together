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
    public class ScheduleDetailsUpdatePacket : IPacket
    {
        public enum DetailsUpdateType
        {
            NAME, ALARM_STATE
        }

        public int ScheduleIndex;
        public string Name;
        public bool AlarmActivated;
        public DetailsUpdateType UpdateType;

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();

            writer.Write(ScheduleIndex);
            writer.Write((int)UpdateType);
            switch (UpdateType)
            {
                case DetailsUpdateType.NAME:
                    writer.Write(Name);
                    break;
                case DetailsUpdateType.ALARM_STATE:
                    writer.Write(AlarmActivated);
                    break;
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();

            ScheduleIndex = reader.ReadInt32();
            UpdateType = (DetailsUpdateType)reader.ReadInt32();
            switch(UpdateType)
            {
                case DetailsUpdateType.NAME:
                    Name = reader.ReadString();
                    break;
                case DetailsUpdateType.ALARM_STATE:
                    AlarmActivated = reader.ReadBoolean();
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

        private void Apply()
        {
            using var _ = Profiler.Scope();

            var schedules = ScheduleManager.Instance.schedules;
            if (schedules == null)
                return;

            Schedule schedule = schedules[ScheduleIndex];
            if (schedule == null)
                return;

            // Screen has not been opened so update cache data
            if(ScheduleScreen.Instance == null)
            {
                switch (UpdateType)
                {
                    case DetailsUpdateType.NAME:
                        schedule.name = Name;
                        break;
                    case DetailsUpdateType.ALARM_STATE:
                        schedule.alarmActivated = AlarmActivated;
                        break;
                }
                return;
            }

            ScheduleScreenEntry entry = ScheduleScreen.Instance.scheduleEntries[ScheduleIndex];
            switch (UpdateType)
            {
                case DetailsUpdateType.NAME:
                    schedule.name = Name;
                    if (entry)
                    {
                        entry.title.SetTitle(Name); // Update the ui display
                        entry.gameObject.name = $"Schedule_{Name}";
                    }
                    break;
                case DetailsUpdateType.ALARM_STATE:
                    schedule.alarmActivated = AlarmActivated;
                    if (entry)
                        entry.RefreshAlarmButton();
                    break;
            }
        }

        public static bool IsApplying = false;
    }
}
