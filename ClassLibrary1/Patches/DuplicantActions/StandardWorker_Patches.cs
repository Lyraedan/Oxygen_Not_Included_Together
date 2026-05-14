using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Misc;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Animation;
using ONI_MP.Networking.Packets.World;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;
using static RancherChore;
using static WorkerBase;

namespace ONI_MP.Patches.DuplicantActions
{
	internal class StandardWorker_Patches
	{

		[HarmonyPatch(typeof(StandardWorker), nameof(StandardWorker.StartWork))]
		public class StandardWorker_StartWork_Patch
		{
			// SKIP WORKABLE
			private static Type[] workablesToSkip =
			{
                typeof(DefragmentationZone),
                typeof(RancherWorkable),
                typeof(LiquidPumpingStation),
				typeof(IceKettleWorkable),
				typeof(Sleepable)
            };

			public static void Postfix(StandardWorker __instance, StartWorkInfo start_work_info)
			{
				using var _ = Profiler.Scope();

				if (__instance.IsNullOrDestroyed())
					return;

				if (start_work_info.IsNullOrDestroyed())
					return;

				if (!Utils.IsHostMinion(__instance))
					return;

				foreach (Type workableType in workablesToSkip)
				{
                    if (start_work_info.workable.GetType() == workableType)
                        return;
                }

                PacketSender.SendToAllClients(new StandardWorker_WorkingState_Packet(__instance, start_work_info.workable, true));
			}
		}

		[HarmonyPatch(typeof(StandardWorker), nameof(StandardWorker.StopWork))]
		public class StandardWorker_StopWork_Patch
		{
			public static void Prefix(StandardWorker __instance)
			{
				using var _ = Profiler.Scope();

				if (__instance.IsNullOrDestroyed())
					return;

				if (!Utils.IsHostMinion(__instance))
					return;

				var workable = __instance.GetWorkable();
				if (workable == null || workable.IsNullOrDestroyed())
					return;

				PacketSender.SendToAllClients(WorkableProgressPacket.CreateHidden(workable), PacketSendMode.ReliableImmediate);

				if (workable.TryGetComponent<ComplexFabricator>(out var fabricator) && fabricator != null && !fabricator.IsNullOrDestroyed())
				{
					PacketSender.SendToAllClients(WorkableProgressPacket.CreateComplexFabricator(fabricator, showProgressBar: false), PacketSendMode.ReliableImmediate);
				}
			}

			public static void Postfix(StandardWorker __instance)
			{
				using var _ = Profiler.Scope();

				if (__instance.IsNullOrDestroyed())
					return;

				if (!Utils.IsHostMinion(__instance))
					return;

				PacketSender.SendToAllClients(new StandardWorker_WorkingState_Packet(__instance,null, false));
			}
		}
	}
}
