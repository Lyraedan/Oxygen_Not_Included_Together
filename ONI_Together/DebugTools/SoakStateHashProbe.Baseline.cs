#if DEBUG
using ONI_Together.Misc.World;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.World;
using UnityEngine;

namespace ONI_Together.DebugTools
{
	internal sealed partial class SoakStateHashProbe
	{
		private void BeginRepairBaselineSegment()
		{
			InvalidateFenceDelivery();
			SuppressAuthoritativeRepair();
			ResumeWorldScan();
			WorldUpdateBatcher.ResumeRepairDispatch();
			EntityPositionHandler.SetCheckpointFrozen(false);
			if (!PopulatePendingClients())
			{
				Abort("remote client set changed before repair baseline warmup");
				return;
			}
			_hostReachedTickBarrier = false;
			_clientReachedTickBarrier = false;
			var config = new SoakTickRunConfig
			{
				RunId = _runId,
				SampleId = _sampleId,
				TickCount = RepairBaselineTickCount,
			};
			if (!SoakTickBarrier.Prepare(config, HostRepairBaselineCompleted))
			{
				Abort("host could not prepare repair baseline warmup");
				return;
			}
			_state = ProbeState.WaitingForTickReady;
			_stateStartedAt = Time.realtimeSinceStartup;
			PacketSender.SendToAllClients(new SoakTickRunPacket
			{
				RunId = _runId,
				SampleId = _sampleId,
				TickCount = RepairBaselineTickCount,
				StartTotalTime = GameClock.Instance.GetTime(),
				IsRepairBaselineWarmup = true,
			}, PacketSendMode.ReliableImmediate);
			DebugConsole.Log($"[SoakHash][BASELINE_WARMUP] segment={_sampleId} " +
			                 HostRepairDiagnostics());
		}

		private void HostRepairBaselineCompleted(SoakTickCompletion completion)
		{
			if (!_running || !_repairBaselineWarmup
			    || _state != ProbeState.WaitingForTickBarrier
			    || completion.RunId != _runId || completion.SampleId != _sampleId
			    || completion.CompletedTicks != RepairBaselineTickCount)
				return;
			_hostReachedTickBarrier = true;
			TryCompleteRepairBaseline();
		}

		private void TryCompleteRepairBaseline()
		{
			if (!_hostReachedTickBarrier || !_clientReachedTickBarrier)
				return;
			WorldUpdateBatcher.Flush();
			if (WorldUpdateBatcher.IsRepairPipelineIdle)
			{
				int warmupSegments = _sampleId;
				SendCancelToConnectedClients();
				_runId = NextRunId();
				_sampleId = 1;
				_completedTicks = 0;
				_repairBaselineWarmup = false;
				DebugConsole.Log($"[SoakHash][BASELINE_READY] segments={warmupSegments} " +
				                 HostRepairDiagnostics());
				BeginSegment();
				return;
			}
			if (Time.realtimeSinceStartup - _repairBaselineStartedAt
			    >= TransportDrainTimeoutSeconds)
			{
				DebugConsole.LogWarning(
					"[SoakHash][BASELINE_TIMEOUT] " + HostRepairDiagnostics());
				Abort("authoritative repair pipeline did not drain during fixed-step warmup");
				return;
			}
			_sampleId++;
			BeginRepairBaselineSegment();
		}
	}
}
#endif
