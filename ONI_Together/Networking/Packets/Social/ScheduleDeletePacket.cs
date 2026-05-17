using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Social
{
	public class ScheduleDeletePacket : IPacket
	{
		public int ScheduleIndex;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(ScheduleIndex);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			ScheduleIndex = reader.ReadInt32();
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

			if (ScheduleManager.Instance == null) return;

			var schedules = Traverse.Create(ScheduleManager.Instance).Field("schedules").GetValue<List<Schedule>>();
			if (schedules == null) return;

			if (ScheduleIndex >= 0 && ScheduleIndex < schedules.Count)
			{
				var schedule = schedules[ScheduleIndex];

				IsApplying = true;
				try
				{
					ScheduleManager.Instance.DeleteSchedule(schedule);
					DebugConsole.Log($"[ScheduleDeletePacket] Deleted schedule {ScheduleIndex}");
				}
				finally
				{
					IsApplying = false;
				}
			}
		}

		public static bool IsApplying = false;
	}
}
