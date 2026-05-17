using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Social
{
	public class ScheduleAssignmentPacket : IPacket
	{
		public int NetId;
		public int ScheduleIndex;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
			writer.Write(ScheduleIndex);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
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

			if (!NetworkIdentityRegistry.TryGet(NetId, out var identity) || identity == null)
			{
				DebugConsole.LogWarning($"[ScheduleAssignmentPacket] NetId {NetId} not found or identity is null.");
				return;
			}

			var schedulable = identity.GetComponent<Schedulable>();
			if (schedulable == null)
			{
				DebugConsole.LogWarning($"[ScheduleAssignmentPacket] NetId {NetId} is not Schedulable.");
				return;
			}

			List<Schedule> schedules = ScheduleManager.Instance.schedules;
			if (schedules == null || ScheduleIndex < 0 || ScheduleIndex >= schedules.Count)
			{
				DebugConsole.LogWarning($"[ScheduleAssignmentPacket] Invalid ScheduleIndex {ScheduleIndex}");
				return;
			}

			Schedule previousSchedule = schedulable.GetSchedule();
            Schedule newSchedule = schedules[ScheduleIndex];
            // Check if already assigned
            if (newSchedule.IsAssigned(schedulable))
				return;

			IsApplying = true;
			try
			{
                previousSchedule.Unassign(schedulable); // Unassign from the old schedule
				newSchedule.Assign(schedulable); // Assign to the new schedule

				DebugConsole.Log($"[ScheduleAssignmentPacket] Assigned {identity.name} to Schedule {ScheduleIndex}");
			}
			finally
			{
				IsApplying = false;
			}
		}

		public static bool IsApplying = false;
	}
}
