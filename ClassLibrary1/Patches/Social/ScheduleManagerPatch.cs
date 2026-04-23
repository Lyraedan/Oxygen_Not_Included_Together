using HarmonyLib;
using KSerialization;
using ONI_MP.Misc;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Social;
using System.Collections.Generic;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Patches.Social
{
	// Sync schedule definitions (name, blocks, alarm)

	public static class SchedulePatch
	{
		[HarmonyPatch(typeof(Schedule), "SetBlockGroup")] // The colored squares (worktime, bath time, sleep time etc)
		public static class SetBlockGroupPatch
		{
			public static void Postfix(Schedule __instance, int idx, ScheduleGroup group)
			{
				using var _ = Profiler.Scope();

				if (!MultiplayerSession.InSession) return;
				if (ScheduleBlockUpdatePacket.IsApplying) return;

				int scheduleIndex = __instance.GetScheduleIndex();
				// Invalid schedule index
				if (scheduleIndex == -1)
					return;

				ScheduleBlockUpdatePacket packet = new ScheduleBlockUpdatePacket() {
					ScheduleIndex = scheduleIndex,
					BlockIndex = idx,
					GroupId = group.Id
				};

                PacketSender.SendToAllOtherPeers(packet);
            }
		}

		[HarmonyPatch(typeof(ScheduleManager), "AddSchedule")]
		public static class AddSchedulePatch
		{
			public static void Postfix(Schedule __result)
			{
				using var _ = Profiler.Scope();

				if (!MultiplayerSession.InSession) return;
				if (ScheduleAddPacket.IsApplying) return;

				ScheduleAddPacket packet = new ScheduleAddPacket()
				{
					Name = __result.name,
					Blocks = __result.blocks,
					AlarmActivated = __result.alarmActivated,
					Duplicated = false
				};

                PacketSender.SendToAllOtherPeers(packet);
            }
		}

		[HarmonyPatch(typeof(ScheduleManager), "DuplicateSchedule")]
        public static class DuplicateSchedulePatch
        {
            public static void Postfix(Schedule __result)
            {
	            using var _ = Profiler.Scope();

                if (!MultiplayerSession.InSession) return;
				if (ScheduleAddPacket.IsApplying) return;

                ScheduleAddPacket packet = new ScheduleAddPacket()
                {
                    Name = __result.name,
                    Blocks = __result.blocks,
                    AlarmActivated = __result.alarmActivated,
                    Duplicated = true
                };

				PacketSender.SendToAllOtherPeers(packet);
            }
        }

        [HarmonyPatch(typeof(ScheduleManager), "DeleteSchedule")]
		public static class DeleteSchedulePatch
		{
			public static void Prefix(ScheduleManager __instance, Schedule schedule)
			{
				using var _ = Profiler.Scope();

				if (!MultiplayerSession.InSession) return;
				if (ScheduleDeletePacket.IsApplying) return;

				int index = schedule.GetScheduleIndex();
				if (index != -1)
				{
					var packet = new ScheduleDeletePacket()
					{
						ScheduleIndex = index
					};

                    PacketSender.SendToAllOtherPeers(packet);
                }
			}
		}
	}
}
