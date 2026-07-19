#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
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
		private const float KeyframeNoProgressTimeoutSeconds = 30f;
		private const float KeyframeAbsoluteTimeoutSeconds = 300f;
		private List<SoakHashDomainKeyframePacket> _keyframeStream;
		private int _keyframeSentEntries;
		private int _keyframeAcknowledgedEntries;
		private int _keyframePageEntryIndex;
		private int _keyframeNextPageIndex;
		private byte[] _keyframeSerializedEntry;
		private int _keyframeSerializedEntryIndex;
		private SoakHashDomainKeyframePagePacket _keyframeOutstandingPage;
		private SoakHashDomainKeyframeBatchPacket _keyframeOutstandingBatch;
		private long _keyframeConnectionGeneration;
		private float _keyframeStreamStartedAt;
		private float _keyframeLastProgressAt;

		private bool SendHashDomainKeyframes()
		{
			if (!TryCaptureKeyframeStream(out var keyframes, out var lifecycleBaseline))
				return false;
			if (!TryCapturePostKeyframeCheckpoint())
				return false;
			_keyframeStream = keyframes;
			_keyframeSentEntries = 0;
			_keyframeAcknowledgedEntries = keyframes.Count == 0 ? -1 : 0;
			_keyframePageEntryIndex = 0;
			_keyframeNextPageIndex = 0;
			_keyframeSerializedEntry = null;
			_keyframeSerializedEntryIndex = -1;
			_keyframeOutstandingPage = null;
			_keyframeOutstandingBatch = null;
			_keyframeConnectionGeneration = 0;
			_keyframeStreamStartedAt = Time.realtimeSinceStartup;
			_keyframeLastProgressAt = _keyframeStreamStartedAt;
			_state = ProbeState.WaitingForKeyframeProgress;
			_stateStartedAt = _keyframeStreamStartedAt;
			var begin = new SoakHashDomainKeyframeBeginPacket
			{
				RunId = _runId,
				SampleId = _sampleId,
				ExpectedEntries = keyframes.Count,
				LifecycleBaseline = lifecycleBaseline,
			};
			ulong clientId = _pendingClients.Single();
			MultiplayerPlayer player = MultiplayerSession.GetPlayer(clientId);
			if (player == null || player.ConnectionGeneration <= 0)
				return false;
			_keyframeConnectionGeneration = player.ConnectionGeneration;
			if (!PacketSender.SendToPlayer(
				    clientId, begin, PacketSendMode.ReliableImmediate)
			    || !SendNextKeyframeTransfer(clientId))
				return false;
			DebugConsole.Log($"[SoakHash][KEYFRAME_STREAM_BEGIN] sample={_sampleId} " +
			                 $"entries={keyframes.Count} sent={_keyframeSentEntries}");
			return true;
		}

		private bool TryCaptureKeyframeStream(
			out List<SoakHashDomainKeyframePacket> keyframes,
			out List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry> baseline)
		{
			keyframes = null;
			baseline = null;
			try
			{
				keyframes = SoakHashDomainKeyframePacket.CaptureAll(_runId, _sampleId);
			}
			catch (Exception ex)
			{
				DebugConsole.LogWarning(
					$"[SoakHash] Hash-domain keyframe capture failed: {ex.Message}");
				return false;
			}
			baseline =
				NetworkIdentityRegistry.GetLifecycleRevisionSnapshot().ToList();
			if (!KeyframesCoverLifecycleBaseline(keyframes, baseline))
			{
				DebugConsole.LogWarning(
					"[SoakHash] Keyframe identities do not cover the lifecycle baseline");
				return false;
			}
			return true;
		}

		private bool SendNextKeyframeTransfer(ulong clientId)
		{
			if (_keyframeStream == null || _keyframeOutstandingPage != null
			    || _keyframeOutstandingBatch != null)
				return false;
			if (_keyframePageEntryIndex >= _keyframeStream.Count)
				return _keyframeStream.Count == 0;
			byte[] first = GetSerializedKeyframeEntry(_keyframePageEntryIndex);
			SoakHashDomainKeyframeBatchPacket batch =
				SoakHashDomainKeyframeBatchPacket.Create(
					_runId, _sampleId, _keyframePageEntryIndex);
			if (!batch.TryAppend(first))
				return SendNextKeyframePage(clientId);
			for (int index = batch.NextEntryIndex;
			     index < _keyframeStream.Count
			     && batch.EntryCount < SoakHashDomainKeyframeBatchPacket.MaxEntriesPerBatch;
			     index++)
			{
				if (!batch.TryAppend(GetSerializedKeyframeEntry(index)))
					break;
			}
			_keyframeOutstandingBatch = batch;
			if (!PacketSender.SendToPlayer(
				    clientId, batch, PacketSendMode.ReliableImmediate))
			{
				_keyframeOutstandingBatch = null;
				return false;
			}
			_keyframeSentEntries = Math.Max(_keyframeSentEntries, batch.NextEntryIndex);
			return true;
		}

		private bool SendNextKeyframePage(ulong clientId)
		{
			SoakHashDomainKeyframePagePacket page = SoakHashDomainKeyframePagePacket.Create(
				_runId, _sampleId, _keyframePageEntryIndex,
				_keyframeNextPageIndex, _keyframeSerializedEntry);
			_keyframeOutstandingPage = page;
			if (!PacketSender.SendToPlayer(
				    clientId, page, PacketSendMode.ReliableImmediate))
			{
				_keyframeOutstandingPage = null;
				return false;
			}
			_keyframeSentEntries = Math.Max(
				_keyframeSentEntries, _keyframePageEntryIndex + 1);
			return true;
		}

		private byte[] GetSerializedKeyframeEntry(int entryIndex)
		{
			if (_keyframeSerializedEntryIndex == entryIndex)
				return _keyframeSerializedEntry;
			_keyframeSerializedEntry = _keyframeStream[entryIndex].SerializeBody();
			_keyframeSerializedEntryIndex = entryIndex;
			return _keyframeSerializedEntry;
		}

		internal static void ReceiveKeyframeProgress(
			SoakKeyframeProgressAckPacket progress, DispatchContext context)
			=> _instance?.AcceptKeyframeProgress(progress, context);

		private void AcceptKeyframeProgress(
			SoakKeyframeProgressAckPacket progress, DispatchContext context)
		{
			if (!_running || _state != ProbeState.WaitingForKeyframeProgress
			    || context.SenderIsHost || !_pendingClients.Contains(context.SenderId)
			    || context.ConnectionGeneration != _keyframeConnectionGeneration
			    || _keyframeStream == null || _keyframeStream.Count != 0)
				return;
			SoakKeyframeProgressResult result = SoakKeyframeProgressAckPacket.Evaluate(
				CurrentKeyframeProgressWindow(), progress);
			if (result == SoakKeyframeProgressResult.Ignore)
				return;
			if (result == SoakKeyframeProgressResult.Invalid)
			{
				AbortAndHardSync("client sent invalid keyframe progress");
				return;
			}
			_keyframeAcknowledgedEntries = progress.ReceivedEntries;
			_keyframeLastProgressAt = Time.realtimeSinceStartup;
			if (result == SoakKeyframeProgressResult.Advanced)
			{
				AbortAndHardSync("empty keyframe stream advanced unexpectedly");
				return;
			}
			if (!progress.ApplySucceeded)
			{
				AbortAndHardSync("client keyframe apply failed");
				return;
			}
			DebugConsole.Log($"[SoakHash][KEYFRAME_STREAM_COMPLETE] sample={_sampleId} " +
			                 $"entries={progress.ReceivedEntries}");
			ResetHostKeyframeStream();
			SendPostKeyframeApplicationFence();
		}

		internal static void ReceiveKeyframePageProgress(
			SoakKeyframePageAckPacket progress, DispatchContext context)
			=> _instance?.AcceptKeyframePageProgress(progress, context);

		private void AcceptKeyframePageProgress(
			SoakKeyframePageAckPacket progress, DispatchContext context)
		{
			if (!_running || _state != ProbeState.WaitingForKeyframeProgress
			    || context.SenderIsHost || !_pendingClients.Contains(context.SenderId)
			    || context.ConnectionGeneration != _keyframeConnectionGeneration
			    || _keyframeStream == null || _keyframeStream.Count == 0)
				return;
			if (_keyframeOutstandingPage == null
			    && progress.RunId == _runId && progress.SampleId == _sampleId
			    && progress.ReceivedEntries <= _keyframeAcknowledgedEntries)
				return;
			SoakKeyframePageProgressResult result = SoakKeyframePageAckPacket.Evaluate(
				new SoakKeyframePageProgressWindow
				{
					RunId = _runId,
					SampleId = _sampleId,
					ExpectedEntries = _keyframeStream.Count,
					OutstandingPage = _keyframeOutstandingPage,
				}, progress);
			if (result == SoakKeyframePageProgressResult.Ignore)
				return;
			if (result == SoakKeyframePageProgressResult.Invalid)
			{
				AbortAndHardSync("client sent invalid keyframe page progress");
				return;
			}

			AdvanceAcceptedKeyframePage(progress);
			if (result == SoakKeyframePageProgressResult.Complete)
			{
				CompleteKeyframePageStream(progress);
				return;
			}
			if (!SendNextKeyframeTransfer(context.SenderId))
				Abort("keyframe page send failed");
		}

		internal static void ReceiveKeyframeBatchProgress(
			SoakKeyframeBatchAckPacket progress, DispatchContext context)
			=> _instance?.AcceptKeyframeBatchProgress(progress, context);

		private void AcceptKeyframeBatchProgress(
			SoakKeyframeBatchAckPacket progress, DispatchContext context)
		{
			if (!_running || _state != ProbeState.WaitingForKeyframeProgress
			    || context.SenderIsHost || !_pendingClients.Contains(context.SenderId)
			    || context.ConnectionGeneration != _keyframeConnectionGeneration
			    || _keyframeStream == null || _keyframeStream.Count == 0)
				return;
			if (_keyframeOutstandingBatch == null
			    && progress.RunId == _runId && progress.SampleId == _sampleId
			    && progress.ReceivedEntries <= _keyframeAcknowledgedEntries)
				return;
			SoakKeyframeBatchProgressResult result = SoakKeyframeBatchAckPacket.Evaluate(
				new SoakKeyframeBatchProgressWindow
				{
					RunId = _runId,
					SampleId = _sampleId,
					ExpectedEntries = _keyframeStream.Count,
					ConnectionGeneration = _keyframeConnectionGeneration,
					OutstandingBatch = _keyframeOutstandingBatch,
				}, progress, context.ConnectionGeneration);
			if (result == SoakKeyframeBatchProgressResult.Ignore)
				return;
			if (result == SoakKeyframeBatchProgressResult.Invalid)
			{
				AbortAndHardSync("client sent invalid keyframe batch progress");
				return;
			}
			AdvanceAcceptedKeyframeBatch(progress);
			if (result == SoakKeyframeBatchProgressResult.Complete)
			{
				CompleteKeyframeStream(progress.ApplySucceeded, progress.ReceivedEntries);
				return;
			}
			if (!SendNextKeyframeTransfer(context.SenderId))
				Abort("keyframe batch send failed");
		}

		private void AdvanceAcceptedKeyframeBatch(SoakKeyframeBatchAckPacket progress)
		{
			_keyframeLastProgressAt = Time.realtimeSinceStartup;
			_keyframeAcknowledgedEntries = progress.ReceivedEntries;
			_keyframePageEntryIndex = progress.ReceivedEntries;
			_keyframeNextPageIndex = 0;
			_keyframeOutstandingBatch = null;
			DiscardAcknowledgedSerializedEntry();
		}

		private void AdvanceAcceptedKeyframePage(SoakKeyframePageAckPacket progress)
		{
			bool entryComplete = progress.AcknowledgedPages
			                     == _keyframeOutstandingPage.PageCount;
			_keyframeLastProgressAt = Time.realtimeSinceStartup;
			_keyframeAcknowledgedEntries = progress.ReceivedEntries;
			_keyframeOutstandingPage = null;
			if (!entryComplete)
			{
				_keyframeNextPageIndex = progress.AcknowledgedPages;
				return;
			}
			_keyframePageEntryIndex++;
			_keyframeNextPageIndex = 0;
			DiscardAcknowledgedSerializedEntry();
		}

		private void CompleteKeyframePageStream(SoakKeyframePageAckPacket progress)
			=> CompleteKeyframeStream(progress.ApplySucceeded, progress.ReceivedEntries);

		private void CompleteKeyframeStream(bool applySucceeded, int receivedEntries)
		{
			if (!applySucceeded)
			{
				AbortAndHardSync("client keyframe apply failed");
				return;
			}
			DebugConsole.Log(
				$"[SoakHash][KEYFRAME_STREAM_COMPLETE] sample={_sampleId} " +
				$"entries={receivedEntries}");
			ResetHostKeyframeStream();
			SendPostKeyframeApplicationFence();
		}

		private void DiscardAcknowledgedSerializedEntry()
		{
			if (_keyframeSerializedEntryIndex >= _keyframePageEntryIndex)
				return;
			_keyframeSerializedEntry = null;
			_keyframeSerializedEntryIndex = -1;
		}

		private SoakKeyframeProgressWindow CurrentKeyframeProgressWindow()
			=> new SoakKeyframeProgressWindow
			{
				RunId = _runId,
				SampleId = _sampleId,
				ExpectedEntries = _keyframeStream.Count,
				SentEntries = _keyframeSentEntries,
				AcknowledgedEntries = _keyframeAcknowledgedEntries,
			};

		private void UpdateKeyframeProgressTimeout()
		{
			float now = Time.realtimeSinceStartup;
			if (KeyframeProgressTimedOut(
				    now - _keyframeLastProgressAt, now - _keyframeStreamStartedAt))
				AbortAndHardSync("keyframe progress timeout");
		}

		internal static bool KeyframeProgressTimedOut(
			float noProgressSeconds, float absoluteSeconds)
			=> noProgressSeconds >= KeyframeNoProgressTimeoutSeconds
			   || absoluteSeconds >= KeyframeAbsoluteTimeoutSeconds;

		private void ResetHostKeyframeStream()
		{
			_keyframeStream = null;
			_keyframeSentEntries = 0;
			_keyframeAcknowledgedEntries = -1;
			_keyframePageEntryIndex = 0;
			_keyframeNextPageIndex = 0;
			_keyframeSerializedEntry = null;
			_keyframeSerializedEntryIndex = -1;
			_keyframeOutstandingPage = null;
			_keyframeOutstandingBatch = null;
			_keyframeConnectionGeneration = 0;
			_keyframeStreamStartedAt = 0f;
			_keyframeLastProgressAt = 0f;
		}

		private static bool KeyframesCoverLifecycleBaseline(
			IEnumerable<SoakHashDomainKeyframePacket> keyframes,
			IEnumerable<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry> baseline)
		{
			var packetIds = new HashSet<int>(keyframes.Select(packet => packet.NetId));
			var liveIds = new HashSet<int>(baseline
				.Where(entry => !entry.Tombstoned).Select(entry => entry.NetId));
			return packetIds.SetEquals(liveIds);
		}

		private bool TryCapturePostKeyframeCheckpoint()
		{
			string failure = "world checkpoint mutation fence is invalid";
			if (!WorldUpdateBatcher.IsFrozenCheckpointValid()
			    || !SoakStateHash.TryCaptureCurrent(
				    out SoakStateHashes hashes, out failure))
			{
				DebugConsole.LogWarning(
					$"[SoakHash] Post-keyframe checkpoint capture failed: {failure}");
				return false;
			}
			if (!HostGridProofMatches(hashes))
			{
				DebugConsole.LogWarning(
					"[SoakHash] Host grid changed after authoritative reconciliation capture");
				return false;
			}
			SoakStateHash.LogClusterRocketRecords("host", "post-keyframe");
			_currentCheckpoint = CreateCheckpoint(hashes, _sampleId == SegmentCount);
			_hostSamples[_currentCheckpoint.SampleId] = _currentCheckpoint;
			return true;
		}
	}
}
#endif
