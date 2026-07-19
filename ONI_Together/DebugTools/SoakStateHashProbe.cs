#if DEBUG
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ONI_Together.Misc.World;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.World;
using UnityEngine;

namespace ONI_Together.DebugTools
{
	internal sealed partial class SoakStateHashProbe : MonoBehaviour
	{
		private const float RequiredGameSeconds = 600f;
		private const int SimTicksPerGameSecond = 60;
		internal const int SegmentTickCount = 1_800;
		internal const int TargetTickCount = 37_800;
		internal const int SegmentCount = TargetTickCount / SegmentTickCount;
		private const float NetworkDrainTimeoutSeconds = 15f;
		private const float AckTimeoutSeconds = 10f;
		private const float TickBarrierTimeoutSeconds = 300f;
		private const float GridReconcileTimeoutSeconds = 300f;
		private const float GridChunkSendIntervalSeconds = 0.03f;
		private const float ReportTimeoutSeconds = 15f;
		private const float TransportDrainTimeoutSeconds = 60f;
		private static SoakStateHashProbe _instance;
		private static int _nextRunId;
		private static bool _firstHostDivergenceRecorded;
		private readonly HashSet<ulong> _pendingClients = new HashSet<ulong>();
		private readonly HashSet<ulong> _pendingFenceClients = new HashSet<ulong>();
		private readonly Dictionary<int, SoakHashCheckpointPacket> _hostSamples = new();
		private readonly Dictionary<int, SoakHashReportPacket> _clientSamples = new();
		private readonly HashSet<int> _comparedSamples = new HashSet<int>();
		private readonly HashSet<int> _rawComparedSamples = new HashSet<int>();
		private ProbeState _state;
		private bool _running;
		private bool _wasPausedAtStart;
		private int _startingSpeed;
		private int _runId;
		private int _sampleId;
		private float _stateStartedAt;
		private float _startTotal;
		private float _repairBaselineStartedAt;
		private int _completedTicks;
		private bool _hostReachedTickBarrier;
		private bool _clientReachedTickBarrier;
		private bool _rawMismatchPendingAbort;
		private bool _rawMismatchSeen;
		private bool _postMismatchSeen;
		private bool _keyframeApplyFailureSeen;
		private bool _authoritativeRepairSuppressed;
		private bool _repairBaselineWarmup;
		private bool _worldScanPaused;
		private bool _fenceDeliveryCompleted;
		private int _fenceDeliveryToken;
		private int _firstRawMismatchSample;
		private int _firstPostMismatchSample;
		private int _firstKeyframeApplyFailureSample;
		private string _firstRawMismatchDomains = "none";
		private string _firstPostMismatchDomains = "none";
		private long _fenceRepairSequenceCut;
		private long _repairRetransmitsAtStart;
		private long _sampleRepairRetransmitsAtStart;
		private SoakHashCheckpointPacket _currentCheckpoint;
		private SoakHashCheckpointPacket _rawHostCheckpoint;

		internal static bool IsRunning => _instance?._running == true;

		private enum ProbeState
		{
			Idle,
			WaitingForTickReady,
			WaitingForTickBarrier,
			WaitingForNetworkDrain,
			WaitingForRawFenceAcks,
			WaitingForKeyframeProgress,
			WaitingForFenceAcks,
			WaitingForGridReconcile,
			WaitingForHashReports,
		}

		private void Awake() => _instance = this;
		internal static void Toggle()
		{
			if (_instance == null)
			{
				DebugConsole.LogWarning("[SoakHash] Probe component is unavailable.");
				return;
			}
			if (_instance._running)
				_instance.Abort("manual stop");
			else
				Start();
		}

		private DebugCommandOutcome TryStart()
		{
			if (!CanStart(out string reason))
			{
				DebugConsole.LogWarning($"[SoakHash] Start rejected: {reason}.");
				return DebugCommandOutcome.Fail("soak", reason);
			}

			SpeedControlScreen speed = SpeedControlScreen.Instance;
			_wasPausedAtStart = speed.IsPaused;
			_startingSpeed = speed.GetSpeed();
			_runId = NextRunId();
			_sampleId = 1;
			_startTotal = 0f;
			_completedTicks = 0;
			_hostSamples.Clear();
			_clientSamples.Clear();
			_comparedSamples.Clear();
			_rawComparedSamples.Clear();
			_pendingFenceClients.Clear();
			_hostReachedTickBarrier = false;
			_clientReachedTickBarrier = false;
			_firstHostDivergenceRecorded = false;
			_rawMismatchSeen = false;
			_postMismatchSeen = false;
			_keyframeApplyFailureSeen = false;
			_firstRawMismatchSample = 0;
			_firstPostMismatchSample = 0;
			_firstKeyframeApplyFailureSample = 0;
			_firstRawMismatchDomains = "none";
			_firstPostMismatchDomains = "none";
			_repairRetransmitsAtStart = 0;
			_repairBaselineWarmup = false;
			_running = true;
			SuppressAuthoritativeRepair();
			if (WorldUpdateBatcher.IsRepairPipelineIdle)
				BeginSegment();
			else
			{
				_repairBaselineWarmup = true;
				_repairBaselineStartedAt = Time.realtimeSinceStartup;
				BeginRepairBaselineSegment();
			}
			if (!_running)
				return DebugCommandOutcome.Fail("soak", "start-aborted");
			return DebugCommandOutcome.Ok("soak", "started");
		}

		private static int NextRunId()
		{
			int id = Interlocked.Increment(ref _nextRunId);
			if (id > 0)
				return id;
			Interlocked.Exchange(ref _nextRunId, 1);
			return 1;
		}

		private static bool CanStart(out string reason)
		{
			reason = string.Empty;
			if (ProductionDesyncRecovery.IsActive)
				reason = "production desync recovery is active";
			else if (!MultiplayerSession.IsHostInSession || RemoteClientCount() != 1)
				reason = "exactly one connected remote client is required";
			else if (!ReadyManager.IsEveryoneReady() || ReadyManager.HasActiveSyncBarrier)
				reason = "all clients must be ready";
			else if (GameClock.Instance == null || SpeedControlScreen.Instance == null)
				reason = "world clock and speed controls are required";
			else if (!SpeedControlScreen.Instance.IsPaused)
				reason = "pause the game and let the pause reach the client before starting";
			return string.IsNullOrEmpty(reason);
		}

		private void Update()
		{
			SoakTickBarrier.Pump();
			PumpClientCheckpointWork();
			if (!_running)
				return;
			if (!CanContinue())
			{
				Abort("session or ready state changed");
				return;
			}
			float elapsed = Time.realtimeSinceStartup - _stateStartedAt;
			switch (_state)
			{
				case ProbeState.WaitingForTickReady:
					if (elapsed >= AckTimeoutSeconds)
						Abort("soak acknowledgement timeout");
					break;
				case ProbeState.WaitingForTickBarrier:
					if (elapsed >= TickBarrierTimeoutSeconds)
						Abort("tick barrier timeout");
					break;
				case ProbeState.WaitingForNetworkDrain:
					UpdateNetworkDrain(elapsed);
					break;
				case ProbeState.WaitingForKeyframeProgress:
					UpdateKeyframeProgressTimeout();
					break;
				case ProbeState.WaitingForFenceAcks:
				case ProbeState.WaitingForRawFenceAcks:
					UpdateFenceTimeout(elapsed);
					break;
				case ProbeState.WaitingForGridReconcile:
					PumpHostGridReconcileSend();
					if (_gridSendCursor?.IsComplete == true
					    && Time.realtimeSinceStartup - _stateStartedAt >= GridReconcileTimeoutSeconds)
						Abort("grid reconcile acknowledgement timeout");
					break;
				case ProbeState.WaitingForHashReports:
					if (elapsed >= ReportTimeoutSeconds)
						Abort("hash report timeout");
					break;
			}
		}

		private void PumpClientCheckpointWork()
		{
			SoakTickBarrier.CancelIfSessionLost();
			PumpClientGridReconcile();
			SendKeyframeProgress();
			UpdateClientPendingFence();
		}

		private static bool CanContinue()
		{
			return MultiplayerSession.IsHostInSession && RemoteClientCount() == 1
				&& ReadyManager.IsEveryoneReady() && !ReadyManager.HasActiveSyncBarrier
				&& GameClock.Instance != null && SpeedControlScreen.Instance != null;
		}

		private static int RemoteClientCount()
		{
			return MultiplayerSession.ConnectedPlayers.Values.Count(player =>
				player.PlayerId != MultiplayerSession.HostUserID && player.Connection != null);
		}

		private bool PopulatePendingClients()
		{
			_pendingClients.Clear();
			foreach (var player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (player.PlayerId != MultiplayerSession.HostUserID && player.Connection != null)
					_pendingClients.Add(player.PlayerId);
			}
			return _pendingClients.Count == 1;
		}

		private void UpdateNetworkDrain(float elapsed)
		{
			WorldUpdateBatcher.Flush();
			PacketSender.DispatchPendingBulkPackets();
			if (WorldUpdateBatcher.HasPendingDispatch || HasPendingBulkPackets())
			{
				if (elapsed >= NetworkDrainTimeoutSeconds)
					Abort("pending world or bulk packet drain timeout");
				return;
			}
			if (!BeginRawFence() && elapsed >= NetworkDrainTimeoutSeconds)
				Abort("world dispatch could not freeze at a stable checkpoint");
		}

		private bool BeginRawFence()
		{
			if (!PopulatePendingClients())
			{
				Abort("remote client set changed before segment fence");
				return false;
			}
			_pendingFenceClients.Clear();
			_pendingFenceClients.UnionWith(_pendingClients);
			if (!WorldUpdateBatcher.TryFreezeRepairDispatch(out _fenceRepairSequenceCut))
				return false;
			if (!SoakStateHash.TryCaptureCurrent(
				    out SoakStateHashes hashes, out string failure))
			{
				Abort("raw checkpoint capture failed: " + failure);
				return false;
			}
			_rawHostCheckpoint = CreateCheckpoint(hashes, _sampleId == SegmentCount);
			var rawFence = new SoakRawFencePacket
			{
				RunId = _runId,
				SampleId = _sampleId,
				CompletedTicks = _completedTicks,
				RepairSequenceCut = _fenceRepairSequenceCut,
			};
			if (!SendFenceWithCompletion(
				    rawFence, ProbeState.WaitingForRawFenceAcks))
				return false;
			DebugConsole.Log($"[SoakHash][RAW_FENCE_SENT] sample={_sampleId} ticks={_completedTicks} " +
				$"repairSequenceCut={_fenceRepairSequenceCut} " + HostRepairDiagnostics());
			return true;
		}

		private void SendPostKeyframeFence()
		{
			if (!SendHashDomainKeyframes())
			{
				Abort("hash-domain keyframe stream failed to start");
				return;
			}
		}

		private void SendPostKeyframeApplicationFence()
		{
			_pendingFenceClients.Clear();
			_pendingFenceClients.UnionWith(_pendingClients);
			var postFence = new SoakSegmentFencePacket
			{
				RunId = _runId,
				SampleId = _sampleId,
				CompletedTicks = _completedTicks,
				RepairSequenceCut = _fenceRepairSequenceCut,
			};
			if (!SendFenceWithCompletion(
				    postFence, ProbeState.WaitingForFenceAcks))
				return;
			DebugConsole.Log($"[SoakHash][POST_KEYFRAME_FENCE_SENT] sample={_sampleId} " +
				$"ticks={_completedTicks} repairSequenceCut={_fenceRepairSequenceCut}");
		}

		private void RecordCheckpoint()
		{
			if (!WorldUpdateBatcher.IsFrozenCheckpointValid())
			{
				Abort("world state mutated after the checkpoint freeze");
				return;
			}
			if (_currentCheckpoint == null || _currentCheckpoint.SampleId != _sampleId)
			{
				Abort("post-keyframe checkpoint was not captured at the keyframe cut");
				return;
			}
			if (!PopulatePendingClients())
			{
				Abort("remote client set changed before hash request");
				return;
			}
			PacketSender.SendToAllClients(_currentCheckpoint, PacketSendMode.ReliableImmediate);
			LogHostSample(_currentCheckpoint);
			_state = ProbeState.WaitingForHashReports;
			_stateStartedAt = Time.realtimeSinceStartup;
		}

		private SoakHashCheckpointPacket CreateCheckpoint(SoakStateHashes hashes, bool isFinal)
		{
			return new SoakHashCheckpointPacket
			{
				RunId = _runId,
				SampleId = _sampleId,
				CompletedTicks = _completedTicks,
				Cycle = GameClock.Instance.GetCycle(),
				CycleTime = GameClock.Instance.GetTimeSinceStartOfCycle(),
				GridRecords = hashes.GridRecords,
				EntityLifecycleRecords = hashes.EntityLifecycleRecords,
				WorldMembershipRecords = hashes.WorldMembershipRecords,
				StorageMembershipRecords = hashes.StorageMembershipRecords,
				ClusterRocketRecords = hashes.ClusterRocketRecords,
				Lifecycle = hashes.Lifecycle,
				IsFinal = isFinal,
				GridHash = hashes.Grid,
				EntityLifecycleHash = hashes.EntityLifecycle,
				WorldMembershipHash = hashes.WorldMembership,
				StorageMembershipHash = hashes.StorageMembership,
				ClusterRocketHash = hashes.ClusterRocket,
			};
		}

		private void CompleteRun()
		{
			ResumeAuthoritativeRepair();
			ResumeWorldScan();
			WorldUpdateBatcher.ResumeRepairDispatch();
			EntityPositionHandler.SetCheckpointFrozen(false);
			if (_completedTicks != TargetTickCount || _comparedSamples.Count != SegmentCount
				|| _rawComparedSamples.Count != SegmentCount
				|| GameClock.Instance.GetTime() - _startTotal < RequiredGameSeconds)
			{
				Abort("segmented run did not cover the fixed-step target");
				return;
			}
			SoakHashCheckpointPacket final = _currentCheckpoint;
			RestoreStartingSpeed();
			_running = false;
			_state = ProbeState.Idle;
			long repairReplays = WorldUpdateBatcher.RepairRetransmitCount - _repairRetransmitsAtStart;
			bool divergence = _rawMismatchSeen || _postMismatchSeen
			                  || _keyframeApplyFailureSeen;
			string outcome = divergence ? "COMPLETE_WITH_DIVERGENCE" : "COMPLETE";
			DebugConsole.Log($"[SoakHash][{outcome}] elapsedGame={GameClock.Instance.GetTime() - _startTotal:F3} " +
				$"elapsedSimTicks={_completedTicks} elapsedSimSeconds={_completedTicks / 60f:F3} " +
				$"rawSamples={_rawComparedSamples.Count} postSamples={_comparedSamples.Count} " +
				$"repairReplaySent={repairReplays} replayAttempted={repairReplays > 0} " +
				$"rawMismatchSeen={_rawMismatchSeen} firstRawMismatchSample={_firstRawMismatchSample} " +
				$"firstRawMismatchDomains={_firstRawMismatchDomains} " +
				$"postMismatchSeen={_postMismatchSeen} firstPostMismatchSample={_firstPostMismatchSample} " +
				$"firstPostMismatchDomains={_firstPostMismatchDomains} " +
				$"keyframeApplyFailureSeen={_keyframeApplyFailureSeen} " +
				$"firstKeyframeApplyFailureSample={_firstKeyframeApplyFailureSample} " +
				$"finalCycle={final.Cycle} finalCycleTime={final.CycleTime:F3} " +
				$"rawPreKeyframeEqual={!_rawMismatchSeen} " +
				$"postKeyframeEqual={!_postMismatchSeen} clientReport=true");
		}

		private static string DifferentDomains(
			SoakHashCheckpointPacket host, SoakHashReportPacket client)
		{
			var domains = new List<string>();
			if (!TimesMatch(host, client)) domains.Add("time");
			if (host.GridRecords != client.GridRecords || !host.GridHash.SequenceEqual(client.GridHash))
				domains.Add("grid");
			if (host.EntityLifecycleRecords != client.EntityLifecycleRecords
			    || !host.EntityLifecycleHash.SequenceEqual(client.EntityLifecycleHash)
			    || !host.Lifecycle.Matches(client.Lifecycle))
				domains.Add("entity");
			if (host.WorldMembershipRecords != client.WorldMembershipRecords
			    || !host.WorldMembershipHash.SequenceEqual(client.WorldMembershipHash))
				domains.Add("world");
			if (host.StorageMembershipRecords != client.StorageMembershipRecords
			    || !host.StorageMembershipHash.SequenceEqual(client.StorageMembershipHash))
				domains.Add("storage");
			if (host.ClusterRocketRecords != client.ClusterRocketRecords
			    || !host.ClusterRocketHash.SequenceEqual(client.ClusterRocketHash))
				domains.Add("clusterRocket");
			return domains.Count == 0 ? "none" : string.Join(",", domains);
		}

		private static void ResumeAtSpeed(int speed)
		{
			if (SpeedControlScreen.Instance.IsPaused)
				SpeedControlScreen.Instance.TogglePause();
			SpeedControlScreen.Instance.SetSpeed(speed);
		}

		private void RestoreStartingSpeed()
		{
			if (_wasPausedAtStart)
			{
				if (SpeedControlScreen.Instance.IsPaused)
					SpeedControlScreen.Instance.TogglePause();
				SpeedControlScreen.Instance.SetSpeed(_startingSpeed);
				if (!SpeedControlScreen.Instance.IsPaused)
					SpeedControlScreen.Instance.TogglePause();
				return;
			}
			ResumeAtSpeed(_startingSpeed);
		}

		private void Abort(string reason)
		{
			ResumeAuthoritativeRepair();
			ResumeWorldScan();
			WorldUpdateBatcher.ResumeRepairDispatch();
			EntityPositionHandler.SetCheckpointFrozen(false);
			if (_runId > 0 && MultiplayerSession.IsHost)
				SendCancelToConnectedClients();
			SoakTickBarrier.Cancel(_runId);
			if (SpeedControlScreen.Instance != null)
				RestoreStartingSpeed();
			_running = false;
			_pendingClients.Clear();
			_pendingFenceClients.Clear();
			_currentCheckpoint = null;
			ResetHostKeyframeStream();
			ResetHostGridReconcile();
			_state = ProbeState.Idle;
			DebugConsole.LogWarning($"[SoakHash][ABORT] reason={reason}");
		}

		private void AbortAndHardSync(string reason)
		{
			Abort(reason);
			if (MultiplayerSession.IsHostInSession)
				GameServerHardSync.PerformHardSync();
		}

		private void SendCancelToConnectedClients()
		{
			var clients = MultiplayerSession.ConnectedPlayers.Values
				.Where(player => player.PlayerId != MultiplayerSession.HostUserID
					&& player.Connection != null)
				.ToArray();
			foreach (var player in clients)
			{
				PacketSender.SendToPlayer(
					player.PlayerId,
					new SoakTickCancelPacket { RunId = _runId },
					PacketSendMode.ReliableImmediate);
			}
		}
	}
}
#endif
