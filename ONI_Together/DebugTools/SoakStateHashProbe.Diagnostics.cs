#if DEBUG
using ONI_Together.Misc.World;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools
{
	internal sealed partial class SoakStateHashProbe
	{
		internal static int RepairBaselineTickCount => 1;

		internal static bool IsFirstCountedSegment(int sampleId, int completedTicks)
			=> sampleId == 1 && completedTicks == 0;

		private static string ClientRepairDiagnostics()
		{
			return $"resolved={WorldUpdatePacket.ClientResolvedRepairSequence} " +
			       $"foreground={WorldUpdatePacket.CurrentClientForegroundSequence} " +
			       $"deferred={WorldUpdatePacket.PendingRepairPacketCount} " +
			       $"observing={WorldUpdateRepairObservability.PendingCount} " +
			       $"superseded={WorldUpdatePacket.ClientSupersededRevision}";
		}

		private static string HostRepairDiagnostics()
		{
			return $"foreground={WorldUpdatePacket.CurrentHostForegroundSequence} " +
			       $"staged={WorldUpdateBatcher.PendingCountForTests(true)} " +
			       $"dispatch={WorldUpdateBatcher.PendingRepairDispatchCountForTests} " +
			       $"journal={WorldUpdateBatcher.RepairJournalPendingCount}";
		}

		private void UpdateFenceTimeout(float elapsed)
		{
			if (WaitForSpecificFenceDelivery(elapsed) || elapsed < AckTimeoutSeconds)
				return;
			DebugConsole.LogWarning(
				$"[SoakHash][CAUSAL_FENCE_INCOMPLETE] sample={_sampleId} " +
				$"cut={_fenceRepairSequenceCut} " + HostRepairDiagnostics());
			Abort("causal state did not reach the segment fence");
		}

		private void StartCountedRun()
		{
			if (_repairBaselineWarmup
			    || !IsFirstCountedSegment(_sampleId, _completedTicks))
				return;
			_startTotal = GameClock.Instance.GetTime();
			_repairRetransmitsAtStart = WorldUpdateBatcher.RepairRetransmitCount;
			DebugConsole.Log($"[SoakHash][START] cycle={GameClock.Instance.GetCycle()} " +
				$"cycleTime={GameClock.Instance.GetTimeSinceStartOfCycle():F3} " +
				$"required={RequiredGameSeconds:F0} " +
				$"duration={TargetTickCount / SimTicksPerGameSecond:F0} " +
				$"segmentTicks={SegmentTickCount} segments={SegmentCount} " +
				$"targetTicks={TargetTickCount} " +
				"domains=grid,entity,world,storage,clusterRocket");
		}
	}
}
#endif
