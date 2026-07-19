#if DEBUG
using System.Linq;
using ONI_Together.Misc.World;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools
{
	internal sealed partial class SoakStateHashProbe
	{
		private static void LogHostSample(SoakHashCheckpointPacket packet)
		{
			DebugConsole.Log($"[SoakHash][POST_KEYFRAME_HOST_SAMPLE] sample={packet.SampleId} ticks={packet.CompletedTicks} cycle={packet.Cycle} " +
				$"cycleTime={packet.CycleTime:F3} gridRecords={packet.GridRecords} " +
				$"entityRecords={packet.EntityLifecycleRecords} worldRecords={packet.WorldMembershipRecords} " +
				$"storageRecords={packet.StorageMembershipRecords} rocketRecords={packet.ClusterRocketRecords} " +
				$"lifecycle=({packet.Lifecycle.ToLogFields()}) " +
				$"grid={SoakStateHash.ToHex(packet.GridHash)} entity={SoakStateHash.ToHex(packet.EntityLifecycleHash)} " +
				$"world={SoakStateHash.ToHex(packet.WorldMembershipHash)} storage={SoakStateHash.ToHex(packet.StorageMembershipHash)} " +
				$"clusterRocket={SoakStateHash.ToHex(packet.ClusterRocketHash)} final={packet.IsFinal}");
		}

		internal static void ReceiveHashReport(SoakHashReportPacket report, DispatchContext context)
		{
			_instance?.AcceptHashReport(report, context);
		}

		private void AcceptHashReport(SoakHashReportPacket report, DispatchContext context)
		{
			if (!_running || _state != ProbeState.WaitingForHashReports || context.SenderIsHost
				|| report.RunId != _runId || report.SampleId != _sampleId
				|| !_pendingClients.Contains(context.SenderId))
				return;
			AcceptFenceReport(report);
		}

		private void AcceptFenceReport(SoakHashReportPacket report)
		{
			if (_currentCheckpoint == null || report.SampleId != _sampleId
				|| report.CompletedTicks != _completedTicks)
				return;
			if (_clientSamples.TryGetValue(report.SampleId, out SoakHashReportPacket existing))
			{
				if (!ReportsEqual(existing, report))
					Abort("duplicate client hash report changed content");
				return;
			}
			_clientSamples[report.SampleId] = report;
			WorldUpdateBatcher.ResumeRepairDispatch();
			bool postMatches = TryCompareSample(report.SampleId);
			if (!postMatches && !_postMismatchSeen)
			{
				_postMismatchSeen = true;
				_firstPostMismatchSample = report.SampleId;
				_firstPostMismatchDomains = DifferentDomains(
					_currentCheckpoint, report);
			}
			if (_rawMismatchPendingAbort && postMatches)
			{
				DebugConsole.LogWarning(
					$"[SoakHash][FIRST_DIVERGENCE_RECOVERY] sample={_sampleId} " +
					"postKeyframeConverged=true");
				_rawMismatchPendingAbort = false;
			}
			if (_currentCheckpoint.IsFinal)
				CompleteRun();
			else
			{
				_sampleId++;
				BeginSegment();
			}
		}

		private bool TryCompareSample(int sampleId)
		{
			if (_comparedSamples.Contains(sampleId)
				|| !_hostSamples.TryGetValue(sampleId, out SoakHashCheckpointPacket host)
				|| !_clientSamples.TryGetValue(sampleId, out SoakHashReportPacket client))
				return false;
			bool matches = LogComparison(host, client, "POST_KEYFRAME");
			_comparedSamples.Add(sampleId);
			return matches;
		}

		private static bool ReportsEqual(SoakHashReportPacket left, SoakHashReportPacket right)
		{
			return left.CompletedTicks == right.CompletedTicks
				&& left.Cycle == right.Cycle
				&& SoakStateHash.NormalizeFloatBits(left.CycleTime)
				== SoakStateHash.NormalizeFloatBits(right.CycleTime)
				&& left.GridRecords == right.GridRecords
				&& left.EntityLifecycleRecords == right.EntityLifecycleRecords
				&& left.WorldMembershipRecords == right.WorldMembershipRecords
				&& left.StorageMembershipRecords == right.StorageMembershipRecords
				&& left.ClusterRocketRecords == right.ClusterRocketRecords
				&& left.Lifecycle.Matches(right.Lifecycle)
				&& left.GridHash.SequenceEqual(right.GridHash)
				&& left.EntityLifecycleHash.SequenceEqual(right.EntityLifecycleHash)
				&& left.WorldMembershipHash.SequenceEqual(right.WorldMembershipHash)
				&& left.StorageMembershipHash.SequenceEqual(right.StorageMembershipHash)
				&& left.ClusterRocketHash.SequenceEqual(right.ClusterRocketHash);
		}

		internal static bool RawAndObservedHashesMatch(
			SoakHashCheckpointPacket raw, SoakHashReportPacket observed)
		{
			return TimesMatch(raw, observed)
				&& raw.GridRecords == observed.GridRecords
				&& raw.EntityLifecycleRecords == observed.EntityLifecycleRecords
				&& raw.WorldMembershipRecords == observed.WorldMembershipRecords
				&& raw.StorageMembershipRecords == observed.StorageMembershipRecords
				&& raw.ClusterRocketRecords == observed.ClusterRocketRecords
				&& raw.Lifecycle.Matches(observed.Lifecycle)
				&& raw.GridHash.SequenceEqual(observed.GridHash)
				&& raw.EntityLifecycleHash.SequenceEqual(observed.EntityLifecycleHash)
				&& raw.WorldMembershipHash.SequenceEqual(observed.WorldMembershipHash)
				&& raw.StorageMembershipHash.SequenceEqual(observed.StorageMembershipHash)
				&& raw.ClusterRocketHash.SequenceEqual(observed.ClusterRocketHash);
		}

		private static bool LogComparison(
			SoakHashCheckpointPacket raw, SoakHashReportPacket observed,
			string phase)
		{
			bool gridEqual = raw.GridRecords == observed.GridRecords
			                 && raw.GridHash.SequenceEqual(observed.GridHash);
			bool entityEqual = raw.EntityLifecycleRecords == observed.EntityLifecycleRecords
			                   && raw.EntityLifecycleHash.SequenceEqual(observed.EntityLifecycleHash)
			                   && raw.Lifecycle.Matches(observed.Lifecycle);
			bool worldEqual = raw.WorldMembershipRecords == observed.WorldMembershipRecords
			                  && raw.WorldMembershipHash.SequenceEqual(observed.WorldMembershipHash);
			bool storageEqual = raw.StorageMembershipRecords == observed.StorageMembershipRecords
			                    && raw.StorageMembershipHash.SequenceEqual(observed.StorageMembershipHash);
			bool rocketEqual = raw.ClusterRocketRecords == observed.ClusterRocketRecords
			                   && raw.ClusterRocketHash.SequenceEqual(observed.ClusterRocketHash);
			bool timeEqual = TimesMatch(raw, observed);
			bool matches = RawAndObservedHashesMatch(raw, observed);
			bool first = !matches && !_firstHostDivergenceRecorded;
			if (first)
				_firstHostDivergenceRecorded = true;
			string line = $"[SoakHash][{phase}_COMPARE] sample={raw.SampleId} cycle={raw.Cycle} " +
				$"cycleTime={raw.CycleTime:F3} observedCycle={observed.Cycle} " +
				$"observedCycleTime={observed.CycleTime:F3} timeEqual={timeEqual} " +
				$"gridEqual={gridEqual} entityEqual={entityEqual} " +
				$"worldEqual={worldEqual} storageEqual={storageEqual} clusterRocketEqual={rocketEqual} " +
				$"rawLifecycle=({raw.Lifecycle.ToLogFields()}) " +
				$"observedLifecycle=({observed.Lifecycle.ToLogFields()}) " +
				$"rawGridRecords={raw.GridRecords} observedGridRecords={observed.GridRecords} " +
				$"rawEntityRecords={raw.EntityLifecycleRecords} observedEntityRecords={observed.EntityLifecycleRecords} " +
				$"rawWorldRecords={raw.WorldMembershipRecords} observedWorldRecords={observed.WorldMembershipRecords} " +
				$"rawStorageRecords={raw.StorageMembershipRecords} observedStorageRecords={observed.StorageMembershipRecords} " +
				$"rawRocketRecords={raw.ClusterRocketRecords} observedRocketRecords={observed.ClusterRocketRecords} " +
				$"rawGrid={SoakStateHash.ToHex(raw.GridHash)} observedGrid={SoakStateHash.ToHex(observed.GridHash)} " +
				$"rawEntity={SoakStateHash.ToHex(raw.EntityLifecycleHash)} observedEntity={SoakStateHash.ToHex(observed.EntityLifecycleHash)} " +
				$"rawWorld={SoakStateHash.ToHex(raw.WorldMembershipHash)} observedWorld={SoakStateHash.ToHex(observed.WorldMembershipHash)} " +
				$"rawStorage={SoakStateHash.ToHex(raw.StorageMembershipHash)} observedStorage={SoakStateHash.ToHex(observed.StorageMembershipHash)} " +
				$"rawClusterRocket={SoakStateHash.ToHex(raw.ClusterRocketHash)} observedClusterRocket={SoakStateHash.ToHex(observed.ClusterRocketHash)} " +
				$"first={first} final={raw.IsFinal}";
			if (first)
				DebugConsole.LogWarning("[SoakHash][FIRST_DIVERGENCE] " + line);
			else
				DebugConsole.Log(line);
			return matches;
		}

		private static bool TimesMatch(
			SoakHashCheckpointPacket host, SoakHashReportPacket client)
		{
			double hostTotal = host.Cycle * 600d + host.CycleTime;
			double clientTotal = client.Cycle * 600d + client.CycleTime;
			return System.Math.Abs(hostTotal - clientTotal) <= 0.01d;
		}
	}
}
#endif
