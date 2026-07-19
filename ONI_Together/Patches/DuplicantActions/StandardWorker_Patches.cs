using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Animation;
using ONI_Together.Networking.Packets.World;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;
using static RancherChore;
using static WorkerBase;
using static ClusterTelescope;

namespace ONI_Together.Patches.DuplicantActions
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
				typeof(Sleepable),
				typeof(Bottler),
				typeof(ClusterTelescopeIdentifyMeteorWorkable)
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

				if (StandardWorker_WorkingState_Packet.TryCreate(
					    __instance, start_work_info.workable, startedWorking: true, out var packet))
				{
					PacketSender.SendToAllClients(packet);
				}
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

				if (WorkableProgressPacket.TryCreate(
					    workable, showProgressBar: false, out var progressPacket))
				{
					PacketSender.SendToAllClients(progressPacket, PacketSendMode.ReliableImmediate);
				}

				if (workable.TryGetComponent<ComplexFabricator>(out var fabricator) && fabricator != null && !fabricator.IsNullOrDestroyed())
				{
					if (WorkableProgressPacket.TryCreateComplexFabricator(
						    fabricator, showProgressBar: false, out var fabricatorPacket))
					{
						PacketSender.SendToAllClients(fabricatorPacket, PacketSendMode.ReliableImmediate);
					}
				}
			}

			public static void Postfix(StandardWorker __instance)
			{
				using var _ = Profiler.Scope();

				if (__instance.IsNullOrDestroyed())
					return;

				if (!Utils.IsHostMinion(__instance))
					return;

				if (StandardWorker_WorkingState_Packet.TryCreate(
					    __instance, workable: null, startedWorking: false, out var packet))
				{
					PacketSender.SendToAllClients(packet);
				}
			}
		}
	}
}
