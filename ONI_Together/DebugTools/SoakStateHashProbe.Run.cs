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
		private void BeginSegment()
		{
			StartCountedRun();
			ResetHostKeyframeStream();
			InvalidateFenceDelivery();
			SuppressAuthoritativeRepair();
			ResumeWorldScan();
			WorldUpdateBatcher.ResumeRepairDispatch();
			EntityPositionHandler.SetCheckpointFrozen(false);
			ResetHostGridReconcile();
			_pendingFenceClients.Clear();
			if (!PopulatePendingClients())
			{
				Abort("remote client set changed before tick segment");
				return;
			}

			_currentCheckpoint = null;
			_rawHostCheckpoint = null;
			_sampleRepairRetransmitsAtStart = WorldUpdateBatcher.RepairRetransmitCount;
			_fenceRepairSequenceCut = 0;
			_hostReachedTickBarrier = false;
			_clientReachedTickBarrier = false;
			_rawMismatchPendingAbort = false;
			SoakTickRunConfig config = new SoakTickRunConfig
			{
				RunId = _runId,
				SampleId = _sampleId,
				TickCount = SegmentTickCount,
			};
			if (!SoakTickBarrier.Prepare(config, HostSegmentCompleted))
			{
				Abort("host could not prepare the tick segment");
				return;
			}
			_state = ProbeState.WaitingForTickReady;
			_stateStartedAt = Time.realtimeSinceStartup;
			PacketSender.SendToAllClients(new SoakTickRunPacket
			{
				RunId = _runId,
				SampleId = _sampleId,
				TickCount = SegmentTickCount,
				StartTotalTime = GameClock.Instance.GetTime(),
			}, PacketSendMode.ReliableImmediate);
		}

		internal static void ReceiveTickReadyAck(SoakTickReadyAckPacket ack, DispatchContext context)
		{
			_instance?.AcceptTickReadyAck(ack, context);
		}

		private void AcceptTickReadyAck(SoakTickReadyAckPacket ack, DispatchContext context)
		{
			if (!_running || _state != ProbeState.WaitingForTickReady || context.SenderIsHost
				|| ack.RunId != _runId || ack.SampleId != _sampleId
				|| !_pendingClients.Contains(context.SenderId))
				return;
			if (!ack.Ready)
			{
				Abort("client could not prepare the tick segment");
				return;
			}
			PacketSender.SendToAllClients(
				new SoakTickStartPacket { RunId = _runId, SampleId = _sampleId },
				PacketSendMode.ReliableImmediate);
			if (!SoakTickBarrier.StartPrepared(_runId, _sampleId))
			{
				Abort("host could not start the prepared tick segment");
				return;
			}
			_state = ProbeState.WaitingForTickBarrier;
			_stateStartedAt = Time.realtimeSinceStartup;
		}

		private void HostSegmentCompleted(SoakTickCompletion completion)
		{
			if (!_running || _state != ProbeState.WaitingForTickBarrier
				|| completion.RunId != _runId || completion.SampleId != _sampleId)
				return;
			if (completion.CompletedTicks != SegmentTickCount)
			{
				Abort("host tick segment stopped before its barrier");
				return;
			}
			_completedTicks = _sampleId * SegmentTickCount;
			_hostReachedTickBarrier = true;
			SuppressAuthoritativeRepair();
			TryReachCausalFence();
		}

		internal static void ReceiveTickBarrierAck(SoakTickBarrierAckPacket ack, DispatchContext context)
		{
			_instance?.AcceptTickBarrierAck(ack, context);
		}

		private void AcceptTickBarrierAck(SoakTickBarrierAckPacket ack, DispatchContext context)
		{
			if (!_running || _state != ProbeState.WaitingForTickBarrier || context.SenderIsHost
				|| ack.RunId != _runId || ack.SampleId != _sampleId
				|| !_pendingClients.Contains(context.SenderId))
				return;
			if (_clientReachedTickBarrier)
			{
				Abort("duplicate client tick barrier acknowledgement");
				return;
			}
			int expectedTicks = _repairBaselineWarmup
				? RepairBaselineTickCount : SegmentTickCount;
			if (!ack.StartedPaused || !ack.IsPaused || ack.CompletedTicks != expectedTicks)
			{
				Abort("client tick segment barrier was invalid");
				return;
			}
			_clientReachedTickBarrier = true;
			if (_repairBaselineWarmup)
				TryCompleteRepairBaseline();
			else
				TryReachCausalFence();
		}

		internal static void ReceiveSegmentFenceAck(
			SoakSegmentFenceAckPacket ack, DispatchContext context)
		{
			_instance?.AcceptSegmentFenceAck(ack, context);
		}

		internal static void ReceiveRawFenceAck(
			SoakRawFenceAckPacket ack, DispatchContext context)
		{
			_instance?.AcceptRawFenceAck(ack, context);
		}

		private void AcceptRawFenceAck(
			SoakRawFenceAckPacket ack, DispatchContext context)
		{
			if (!_running || _state != ProbeState.WaitingForRawFenceAcks
			    || context.SenderIsHost || ack.RunId != _runId
			    || ack.SampleId != _sampleId || ack.CompletedTicks != _completedTicks
			    || ack.RepairSequenceCut != _fenceRepairSequenceCut
			    || !_pendingClients.Contains(context.SenderId))
				return;
			if (!_pendingFenceClients.Remove(context.SenderId))
			{
				Abort("duplicate raw fence acknowledgement");
				return;
			}
			if (_pendingFenceClients.Count != 0)
				return;
			if (!WorldUpdateBatcher.IsFrozenCheckpointValid())
			{
				Abort("world state mutated before the raw hash fence");
				return;
			}
			_rawComparedSamples.Add(_sampleId);
			bool rawMatches = _rawHostCheckpoint != null && LogComparison(
				_rawHostCheckpoint, ack.RawObserved, "RAW_PRE_KEYFRAME");
			if (!rawMatches && !_rawMismatchSeen)
			{
				_rawMismatchSeen = true;
				_firstRawMismatchSample = _sampleId;
				_firstRawMismatchDomains = _rawHostCheckpoint == null
					? "missingHostCheckpoint"
					: DifferentDomains(_rawHostCheckpoint, ack.RawObserved);
			}
			long sampleReplays = WorldUpdateBatcher.RepairRetransmitCount
				- _sampleRepairRetransmitsAtStart;
			DebugConsole.Log($"[SoakHash][RAW_TRANSPORT] sample={_sampleId} " +
				$"repairReplaySent={sampleReplays} replayAttempted={sampleReplays > 0}");
			_rawMismatchPendingAbort = !rawMatches;
			BeginGridReconcile();
		}

		private void AcceptSegmentFenceAck(
			SoakSegmentFenceAckPacket ack, DispatchContext context)
		{
			if (!_running || _state != ProbeState.WaitingForFenceAcks || context.SenderIsHost
			    || ack.RunId != _runId || ack.SampleId != _sampleId
			    || ack.CompletedTicks != _completedTicks
			    || ack.RepairSequenceCut != _fenceRepairSequenceCut
			    || !_pendingClients.Contains(context.SenderId))
				return;
			if (!_pendingFenceClients.Remove(context.SenderId))
			{
				Abort("duplicate segment fence acknowledgement");
				return;
			}
			if (_pendingFenceClients.Count != 0)
				return;
			if (RequiresAuthoritativeHardSync(ack.KeyframeApplied))
			{
				_keyframeApplyFailureSeen = true;
				if (_firstKeyframeApplyFailureSample == 0)
					_firstKeyframeApplyFailureSample = _sampleId;
				DebugConsole.LogWarning(
					$"[SoakHash][KEYFRAME_APPLY_FAILED] sample={_sampleId}");
				AbortAndHardSync(
					"client keyframe apply failed; authoritative hard sync required");
				return;
			}
			DebugConsole.Log($"[SoakHash][FENCE_ACK] sample={_sampleId} clients={_pendingClients.Count} " +
				$"repairSequenceCut={_fenceRepairSequenceCut} " +
				$"keyframeApplied={ack.KeyframeApplied}");
			RecordCheckpoint();
		}

		internal static bool RequiresAuthoritativeHardSync(bool keyframeApplied)
			=> !keyframeApplied;

		private void TryReachCausalFence()
		{
			if (!_hostReachedTickBarrier || !_clientReachedTickBarrier)
				return;
			PauseWorldScan();
			if (WorldStateSyncer.Instance == null
			    || !WorldStateSyncer.Instance.QueueChangedCellsForCheckpoint())
			{
				Abort("full changed-cell checkpoint sweep failed");
				return;
			}
			EntityPositionHandler.SetCheckpointFrozen(true);
			WorldUpdateBatcher.Flush();
			PacketSender.DispatchPendingBulkPackets();
			_state = ProbeState.WaitingForNetworkDrain;
			_stateStartedAt = UnityEngine.Time.realtimeSinceStartup;
		}
	}
}
#endif
