#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using ONI_Together.DebugTools;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World
{
	internal sealed class SoakHashDomainKeyframeContext
	{
		internal int RunId;
		internal int SampleId;
		internal int ExpectedEntries;
		internal bool PagedTransport;
		internal IReadOnlyList<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>
			LifecycleBaseline;
	}

	internal sealed class SoakHashDomainKeyframeRecord
	{
		internal int RunId;
		internal int SampleId;
		internal int EntryIndex;
		internal bool Applied;
	}

	internal static class SoakHashDomainKeyframeTracker
	{
		private static int _runId;
		private static int _sampleId;
		private static int _expectedEntries;
		private static bool _failed;
		private static bool _applyAttempted;
		private static bool _pagedTransport;
		private static int _applyFrame = -1;
		private static bool _deferredValidationFinished;
		private static int _lastProgressSent = -1;
		private static readonly HashSet<int> ReceivedEntries = new();
		private static readonly Dictionary<int, SoakHashDomainKeyframePacket> Buffered = new();
		private static IReadOnlyList<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>
			_lifecycleBaseline = new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>();

		internal static void Begin(int runId, int sampleId, int expectedEntries)
			=> Begin(new SoakHashDomainKeyframeContext
			{
				RunId = runId,
				SampleId = sampleId,
				ExpectedEntries = expectedEntries,
				LifecycleBaseline =
					new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>(),
			});

		internal static void Begin(SoakHashDomainKeyframeContext context)
		{
			_runId = context.RunId;
			_sampleId = context.SampleId;
			_expectedEntries = context.ExpectedEntries;
			_failed = false;
			_applyAttempted = false;
			_pagedTransport = context.PagedTransport;
			_applyFrame = -1;
			_deferredValidationFinished = false;
			_lastProgressSent = -1;
			ReceivedEntries.Clear();
			Buffered.Clear();
			SoakKeyframePageReceiver.Reset();
			SpawnPrefabPacket.ResetSnapshotDiagnostics();
			_lifecycleBaseline = context.LifecycleBaseline;
			NetworkIdentityRegistry.SetLifecyclePruneFrozen(true);
			if (_expectedEntries == 0)
				ApplyBufferedKeyframes();
		}

		internal static bool RecordPacket(SoakHashDomainKeyframePacket packet)
			=> !_pagedTransport && RecordValidatedPacket(packet);

		internal static bool RecordPagedPacket(SoakHashDomainKeyframePacket packet)
			=> _pagedTransport && RecordValidatedPacket(packet);

		internal static bool RecordPagedBatch(
			IReadOnlyList<SoakHashDomainKeyframePacket> packets)
		{
			if (!_pagedTransport || packets == null || packets.Count == 0)
				return false;
			int firstEntry = ContiguousReceivedEntries();
			for (int index = 0; index < packets.Count; index++)
			{
				SoakHashDomainKeyframePacket packet = packets[index];
				if (packet == null || packet.RunId != _runId || packet.SampleId != _sampleId
				    || packet.EntryIndex != firstEntry + index
				    || packet.EntryIndex >= _expectedEntries
				    || ReceivedEntries.Contains(packet.EntryIndex))
					return false;
			}
			foreach (SoakHashDomainKeyframePacket packet in packets)
			{
				ReceivedEntries.Add(packet.EntryIndex);
				Buffered.Add(packet.EntryIndex, packet);
			}
			if (ReceivedEntries.Count == _expectedEntries)
				ApplyBufferedKeyframes();
			return true;
		}

		private static bool RecordValidatedPacket(SoakHashDomainKeyframePacket packet)
		{
			if (!TryRecordIndex(packet.RunId, packet.SampleId, packet.EntryIndex))
				return false;
			Buffered.Add(packet.EntryIndex, packet);
			if (ReceivedEntries.Count == _expectedEntries)
				ApplyBufferedKeyframes();
			return true;
		}

		private static void ApplyBufferedKeyframes()
		{
			SoakHashDomainKeyframePacket[] packets = Buffered
				.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();
			CompleteApply(() => SoakHashDomainKeyframePacket.TryApplyAll(
				packets, _lifecycleBaseline));
		}

		private static void CompleteApply(Func<bool> apply)
		{
			_applyAttempted = true;
			try
			{
				_failed = !apply();
			}
			catch (Exception ex)
			{
				_failed = true;
				DebugConsole.LogWarning(
					$"[SoakHash] Keyframe apply failed: {ex.GetType().Name}: {ex.Message}");
			}
			_applyFrame = Time.frameCount;
			if (_failed)
				NetworkIdentityRegistry.SetLifecyclePruneFrozen(false);
		}

		internal static void ApplyForTests(Func<bool> apply) => CompleteApply(apply);

		internal static bool Record(SoakHashDomainKeyframeRecord record)
		{
			if (record == null
			    || !TryRecordIndex(record.RunId, record.SampleId, record.EntryIndex))
				return false;
			_failed |= !record.Applied;
			_applyAttempted = ReceivedEntries.Count == _expectedEntries;
			return true;
		}

		private static bool TryRecordIndex(int runId, int sampleId, int entryIndex)
		{
			return runId == _runId && sampleId == _sampleId
			       && entryIndex >= 0 && entryIndex < _expectedEntries
			       && ReceivedEntries.Add(entryIndex);
		}

		internal static bool IsComplete(int runId, int sampleId)
			=> HasFinished(runId, sampleId) && !_failed;

		internal static bool HasFinished(int runId, int sampleId)
			=> runId == _runId && sampleId == _sampleId && _applyAttempted
			   && ReceivedEntries.Count == _expectedEntries;

		internal static bool ApplySucceeded(int runId, int sampleId)
			=> HasFinished(runId, sampleId) && !_failed;

		internal static void LogStateDrift(
			int runId, int sampleId,
			IReadOnlyList<SoakEntityState> entityStates,
			IReadOnlyList<SoakWorldMembershipState> worldStates)
		{
			if (!HasFinished(runId, sampleId))
				return;
			SoakEntityLifecycleDiagnostics.LogDrift(
				sampleId, Buffered.Values, _lifecycleBaseline, entityStates);
			SoakWorldMembershipDiagnostics.LogDrift(
				sampleId, Buffered.Values, worldStates);
		}

		internal static bool TryFinalizeDeferredValidation(int runId, int sampleId)
		{
			if (!HasFinished(runId, sampleId))
				return false;
			if (_deferredValidationFinished)
				return true;
			if (!IsDeferredValidationFrame(_applyFrame, Time.frameCount))
				return false;
			NetworkIdentityRegistry.LifecycleMembershipValidationResult membership =
				NetworkIdentityRegistry.ValidateCurrentLifecycleMembership(_lifecycleBaseline);
			_failed |= !membership.IsValid;
			_deferredValidationFinished = true;
			NetworkIdentityRegistry.SetLifecyclePruneFrozen(false);
			if (!membership.IsValid)
			{
				DebugConsole.LogWarning(
					$"[SoakKeyframe] deferred lifecycle mismatch missing={membership.MissingLiveCount} " +
					$"unexpected={membership.UnexpectedLiveCount} tombstoned={membership.TombstonedLiveCount} " +
					$"unassigned={membership.UnassignedLiveCount}");
				LogMissingLifecycleDiagnostics();
			}
			return true;
		}

		private static void LogMissingLifecycleDiagnostics()
		{
			foreach (NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry entry
			         in _lifecycleBaseline.Where(entry => !entry.Tombstoned
			                                           && !NetworkIdentityRegistry.Exists(entry.NetId)))
			{
				SpawnPrefabPacket descriptor = entry.Descriptor
				                                 ?? Buffered.Values.FirstOrDefault(
					                                 packet => packet.NetId == entry.NetId)
					                                 ?.LifecycleSnapshot;
				DebugConsole.LogWarning(
					$"[SoakKeyframe] missing NetId {entry.NetId}: " +
					$"hash={descriptor?.Hash ?? 0}, world={descriptor?.WorldId ?? -1}, " +
					$"bindExisting={descriptor?.BindExistingOnly ?? false}, " +
					$"apply={SpawnPrefabPacket.GetSnapshotDiagnostic(entry.NetId)}");
			}
		}

		internal static bool IsDeferredValidationFrame(int applyFrame, int currentFrame)
			=> applyFrame < 0 || currentFrame >= applyFrame + 2;

		internal static bool TryGetProgress(
			out SoakKeyframeProgressAckPacket progress)
		{
			progress = null;
			if (_runId <= 0 || _sampleId <= 0
			    || _pagedTransport && _expectedEntries > 0)
				return false;
			int contiguous = ContiguousReceivedEntries();
			bool finished = HasFinished(_runId, _sampleId);
			bool windowComplete = contiguous > 0
			                      && contiguous % SoakKeyframeProgressAckPacket.WindowEntries == 0;
			if (contiguous <= _lastProgressSent
			    || !finished && contiguous != _expectedEntries && !windowComplete)
				return false;
			progress = new SoakKeyframeProgressAckPacket
			{
				RunId = _runId,
				SampleId = _sampleId,
				ReceivedEntries = contiguous,
				ApplyFinished = finished,
				ApplySucceeded = finished && !_failed,
			};
			return true;
		}

		internal static void CommitProgress(SoakKeyframeProgressAckPacket progress)
		{
			if (progress != null && progress.RunId == _runId
			    && progress.SampleId == _sampleId
			    && progress.ReceivedEntries > _lastProgressSent)
				_lastProgressSent = progress.ReceivedEntries;
		}

		private static int ContiguousReceivedEntries()
		{
			int contiguous = 0;
			while (ReceivedEntries.Contains(contiguous))
				contiguous++;
			return contiguous;
		}

		internal static void Reset()
		{
			_runId = 0;
			_sampleId = 0;
			_expectedEntries = 0;
			_failed = false;
			_applyAttempted = false;
			_pagedTransport = false;
			_applyFrame = -1;
			_deferredValidationFinished = false;
			_lastProgressSent = -1;
			ReceivedEntries.Clear();
			Buffered.Clear();
			SoakKeyframePageReceiver.Reset();
			_lifecycleBaseline =
				new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>();
			NetworkIdentityRegistry.SetLifecyclePruneFrozen(false);
		}

		internal static bool UsesPagedTransport => _pagedTransport;
	}
}
#endif
