#if DEBUG
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Patches.GamePatches;

namespace ONI_Together.DebugTools
{
	internal sealed partial class SoakStateHashProbe
	{
		private static int _clientRunId;
		private static int _clientCompletedSampleId;
		private static int _clientRawFencedSampleId;
		private static int _clientFencedSampleId;
		private static bool _clientRepairBaselineWarmup;
		private static SoakRawFencePacket _clientPendingRawFence;
		private static SoakSegmentFencePacket _clientPendingFence;
		private sealed class HashReportContext
		{
			internal int RunId;
			internal int SampleId;
			internal int CompletedTicks;
			internal SoakStateHashes Hashes;
		}

		internal static void ReceiveTickCancel(int runId)
		{
			bool barrierCancelled = SoakTickBarrier.Cancel(runId);
			if (_clientRunId != runId && !barrierCancelled)
				return;
			ResetClientSegmentSequence();
			SoakTickBarrier.EnsureLocallyPaused();
		}

		internal static void ReceiveRawFence(SoakRawFencePacket fence)
		{
			if (!MultiplayerSession.IsClient || SpeedControlScreen.Instance?.IsPaused != true
			    || fence.RunId != _clientRunId
			    || fence.SampleId != _clientCompletedSampleId
			    || fence.CompletedTicks != fence.SampleId * SegmentTickCount
			    || _clientRawFencedSampleId == fence.SampleId)
				return;
			if (_clientPendingRawFence != null
			    && (_clientPendingRawFence.RunId != fence.RunId
			        || _clientPendingRawFence.SampleId != fence.SampleId
			        || _clientPendingRawFence.RepairSequenceCut != fence.RepairSequenceCut))
				return;
			DebugConsole.Log($"[SoakHash][RAW_FENCE_RECEIVED] sample={fence.SampleId} " +
			                 $"cut={fence.RepairSequenceCut} " + ClientRepairDiagnostics());
			_clientPendingRawFence = fence;
			UpdateClientPendingFence();
		}

		internal static void ReceiveSegmentFence(SoakSegmentFencePacket fence)
		{
			if (!MultiplayerSession.IsClient || SpeedControlScreen.Instance?.IsPaused != true
			    || fence.RunId != _clientRunId
			    || fence.SampleId != _clientCompletedSampleId
			    || fence.CompletedTicks != fence.SampleId * SegmentTickCount
			    || _clientRawFencedSampleId != fence.SampleId
			    || _clientFencedSampleId == fence.SampleId)
				return;
			if (_clientPendingFence != null
			    && (_clientPendingFence.RunId != fence.RunId
			        || _clientPendingFence.SampleId != fence.SampleId
			        || _clientPendingFence.RepairSequenceCut != fence.RepairSequenceCut))
				return;
			_clientPendingFence = fence;
			UpdateClientPendingFence();
		}

		private static void UpdateClientPendingFence()
		{
			if (TryAcknowledgeRawFence())
				return;
			SoakSegmentFencePacket fence = _clientPendingFence;
			if (fence == null || !MultiplayerSession.IsClient
			    || SpeedControlScreen.Instance?.IsPaused != true
			    || !SoakHashDomainKeyframeTracker.HasFinished(fence.RunId, fence.SampleId)
			    || !SoakHashDomainKeyframeTracker.TryFinalizeDeferredValidation(
				    fence.RunId, fence.SampleId)
			    || !CanAcknowledgeRepairFence(
				    WorldUpdatePacket.ClientResolvedRepairSequence, fence.RepairSequenceCut))
				return;
			var ack = new SoakSegmentFenceAckPacket
			{
				RunId = fence.RunId,
				SampleId = fence.SampleId,
				CompletedTicks = fence.CompletedTicks,
				RepairSequenceCut = fence.RepairSequenceCut,
				KeyframeApplied = SoakHashDomainKeyframeTracker.ApplySucceeded(
					fence.RunId, fence.SampleId),
			};
			if (!PacketSender.SendToHost(ack, PacketSendMode.ReliableImmediate))
				return;
			DebugConsole.Log($"[SoakHash][POST_KEYFRAME_FENCE_ACK_SENT] sample={fence.SampleId} " +
			                 $"cut={fence.RepairSequenceCut} " +
			                 $"resolved={WorldUpdatePacket.ClientResolvedRepairSequence}");
			_clientFencedSampleId = fence.SampleId;
			_clientPendingFence = null;
		}

		internal static void SendKeyframeProgress()
		{
			if (!MultiplayerSession.IsClient)
				return;
			if (SoakHashDomainKeyframeTracker.UsesPagedTransport)
			{
				SendKeyframePageProgress();
				return;
			}
			if (!SoakHashDomainKeyframeTracker.TryGetProgress(out var progress))
				return;
			if (!PacketSender.SendToHost(progress, PacketSendMode.ReliableImmediate))
				return;
			SoakHashDomainKeyframeTracker.CommitProgress(progress);
		}

		private static void SendKeyframePageProgress()
		{
			if (!SoakKeyframePageReceiver.TryGetPendingAck(out var progress)
			    || !PacketSender.SendToHost(progress, PacketSendMode.ReliableImmediate))
				return;
			SoakKeyframePageReceiver.CommitAck(progress);
		}

		private static bool TryAcknowledgeRawFence()
		{
			SoakRawFencePacket fence = _clientPendingRawFence;
			if (fence == null || !MultiplayerSession.IsClient
			    || GameClock.Instance == null
			    || SpeedControlScreen.Instance?.IsPaused != true
			    || !CanAcknowledgeRepairFence(
				    WorldUpdatePacket.ClientResolvedRepairSequence, fence.RepairSequenceCut))
				return false;
			if (!SoakStateHash.TryCaptureCurrent(
				    out SoakStateHashes hashes, out string failure))
			{
				DebugConsole.LogWarning("[SoakHash] Raw client capture rejected: " + failure);
				return false;
			}
			var ack = new SoakRawFenceAckPacket
			{
				RunId = fence.RunId,
				SampleId = fence.SampleId,
				CompletedTicks = fence.CompletedTicks,
				RepairSequenceCut = fence.RepairSequenceCut,
				RawObserved = CreateHashReport(new HashReportContext
				{
					RunId = fence.RunId,
					SampleId = fence.SampleId,
					CompletedTicks = fence.CompletedTicks,
					Hashes = hashes,
				}),
			};
			if (!PacketSender.SendToHost(ack, PacketSendMode.ReliableImmediate))
				return false;
			DebugConsole.Log($"[SoakHash][RAW_FENCE_ACK_SENT] sample={fence.SampleId} " +
			                 $"cut={fence.RepairSequenceCut} " +
			                 $"resolved={WorldUpdatePacket.ClientResolvedRepairSequence}");
			_clientRawFencedSampleId = fence.SampleId;
			_clientPendingRawFence = null;
			return true;
		}

		internal static bool CanAcknowledgeRepairFence(long resolvedSequence, long sequenceCut)
			=> sequenceCut >= 0 && resolvedSequence >= sequenceCut;

		internal static void SendHashReport(SoakHashCheckpointPacket checkpoint)
		{
			if (!MultiplayerSession.IsClient || SpeedControlScreen.Instance == null || GameClock.Instance == null)
				return;
			if (checkpoint.RunId != _clientRunId || checkpoint.SampleId != _clientCompletedSampleId
				|| checkpoint.SampleId != _clientFencedSampleId
				|| checkpoint.CompletedTicks != checkpoint.SampleId * SegmentTickCount
				|| !SpeedControlScreen.Instance.IsPaused
				|| !TryAlignClientTime(checkpoint.Cycle * 600f + checkpoint.CycleTime))
				return;
			if (!SoakStateHash.TryCaptureCurrent(
				    out SoakStateHashes hashes, out string failure))
			{
				DebugConsole.LogWarning("[SoakHash] Post-keyframe client capture rejected: " + failure);
				return;
			}
			SoakHashDomainKeyframeTracker.LogStateDrift(
				checkpoint.RunId, checkpoint.SampleId,
				hashes.EntityStates, hashes.WorldStates);
			SoakStateHash.LogClusterRocketRecords("client", "post-keyframe");
			PacketSender.SendToHost(CreateHashReport(new HashReportContext
			{
				RunId = checkpoint.RunId,
				SampleId = checkpoint.SampleId,
				CompletedTicks = checkpoint.CompletedTicks,
				Hashes = hashes,
			}), PacketSendMode.ReliableImmediate);
			if (checkpoint.IsFinal)
				ResetClientSegmentSequence();
		}

		internal static void ReceiveTickPrepare(SoakTickRunPacket packet)
		{
			if (!MultiplayerSession.IsClient || SpeedControlScreen.Instance == null || GameClock.Instance == null)
				return;
			if (SoakTickBarrier.MatchesCurrent(packet.RunId, packet.SampleId))
				return;
			if (ShouldSupersedeClientRun(_clientRunId, packet.RunId, packet.SampleId))
			{
				SoakTickBarrier.Cancel(_clientRunId);
				SoakTickBarrier.EnsureLocallyPaused();
				ResetClientSegmentSequence();
			}
			bool startedPaused = SpeedControlScreen.Instance.IsPaused;
			int expectedTicks = packet.IsRepairBaselineWarmup
				? RepairBaselineTickCount : SegmentTickCount;
			bool valid = packet.TickCount == expectedTicks && SoakTickBarrier.IsNextSegment(
				(_clientRunId, _clientCompletedSampleId), packet.RunId, packet.SampleId);
			if (!valid)
				return;
			ResetClientGridReconcile();
			bool aligned = startedPaused && valid && TryAlignClientTime(packet.StartTotalTime);
			SoakTickRunConfig config = new SoakTickRunConfig
			{
				RunId = packet.RunId,
				SampleId = packet.SampleId,
				TickCount = packet.TickCount,
			};
			bool prepared = aligned && SoakTickBarrier.Prepare(config, ClientSegmentCompleted);
			if (prepared)
			{
				_clientRunId = packet.RunId;
				_clientRepairBaselineWarmup = packet.IsRepairBaselineWarmup;
			}
			PacketSender.SendToHost(new SoakTickReadyAckPacket
			{
				RunId = packet.RunId,
				SampleId = packet.SampleId,
				Ready = prepared,
			}, PacketSendMode.ReliableImmediate);
		}

		internal static bool ShouldSupersedeClientRun(
			int currentRunId, int incomingRunId, int incomingSampleId)
		{
			return currentRunId > 0 && incomingRunId > 0
			       && incomingRunId != currentRunId && incomingSampleId == 1;
		}

		internal static void ReceiveTickStart(int runId, int sampleId)
		{
			if (!MultiplayerSession.IsClient || !SoakTickBarrier.IsPrepared(runId, sampleId))
				return;
			if (SoakTickBarrier.StartPrepared(runId, sampleId))
				return;
			SoakTickBarrier.EnsureLocallyPaused();
			SendTickBarrierAck(new SoakTickBarrierAckPacket
			{
				RunId = runId,
				SampleId = sampleId,
				CompletedTicks = 0,
				StartedPaused = true,
				IsPaused = true,
			});
		}

		private static void ClientSegmentCompleted(SoakTickCompletion completion)
		{
			int expectedTicks = _clientRepairBaselineWarmup
				? RepairBaselineTickCount : SegmentTickCount;
			if (completion.RunId != _clientRunId
				|| completion.SampleId != _clientCompletedSampleId + 1
				|| completion.CompletedTicks != expectedTicks)
				return;
			_clientCompletedSampleId = completion.SampleId;
			SendTickBarrierAck(new SoakTickBarrierAckPacket
			{
				RunId = completion.RunId,
				SampleId = completion.SampleId,
				CompletedTicks = completion.CompletedTicks,
				StartedPaused = true,
				IsPaused = SpeedControlScreen.Instance?.IsPaused == true,
			});
		}

		private static void SendTickBarrierAck(SoakTickBarrierAckPacket ack)
		{
			PacketSender.SendToHost(ack, PacketSendMode.ReliableImmediate);
		}

		internal static void ResetClientSegmentSequence()
		{
			_clientRunId = 0;
			_clientCompletedSampleId = 0;
			_clientRawFencedSampleId = 0;
			_clientFencedSampleId = 0;
			_clientRepairBaselineWarmup = false;
			_clientPendingRawFence = null;
			_clientPendingFence = null;
			SoakHashDomainKeyframeTracker.Reset();
			ResetClientGridReconcile();
		}

		private static SoakHashReportPacket CreateHashReport(HashReportContext context)
		{
			SoakStateHashes hashes = context.Hashes;
			return new SoakHashReportPacket
			{
				RunId = context.RunId,
				SampleId = context.SampleId,
				CompletedTicks = context.CompletedTicks,
				Cycle = GameClock.Instance.GetCycle(),
				CycleTime = GameClock.Instance.GetTimeSinceStartOfCycle(),
				GridRecords = hashes.GridRecords,
				EntityLifecycleRecords = hashes.EntityLifecycleRecords,
				WorldMembershipRecords = hashes.WorldMembershipRecords,
				StorageMembershipRecords = hashes.StorageMembershipRecords,
				ClusterRocketRecords = hashes.ClusterRocketRecords,
				Lifecycle = hashes.Lifecycle,
				GridHash = hashes.Grid,
				EntityLifecycleHash = hashes.EntityLifecycle,
				WorldMembershipHash = hashes.WorldMembership,
				StorageMembershipHash = hashes.StorageMembership,
				ClusterRocketHash = hashes.ClusterRocket,
			};
		}

		private static bool TryAlignClientTime(float totalTime)
		{
			if (GameClock.Instance.GetTime() > totalTime + 0.01f)
				return false;
			GameClockPatch.allowAddTimeForSetTime = true;
			try
			{
				GameClock.Instance.SetTime(totalTime);
				return System.Math.Abs(GameClock.Instance.GetTime() - totalTime) <= 0.01f;
			}
			finally
			{
				GameClockPatch.allowAddTimeForSetTime = false;
			}
		}
	}
}
#endif
