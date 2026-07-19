#if DEBUG
using System.Linq;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using UnityEngine;

namespace ONI_Together.DebugTools
{
	internal sealed partial class SoakStateHashProbe
	{
		private void SuppressAuthoritativeRepair()
		{
			if (_authoritativeRepairSuppressed)
				return;
			WorldStateSyncer.SetAuthoritativeRepairSuppressed(true);
			_authoritativeRepairSuppressed = true;
		}

		private void ResumeAuthoritativeRepair()
		{
			InvalidateFenceDelivery();
			if (!_authoritativeRepairSuppressed)
				return;
			WorldStateSyncer.SetAuthoritativeRepairSuppressed(false);
			_authoritativeRepairSuppressed = false;
		}

		private void PauseWorldScan()
		{
			if (_worldScanPaused)
				return;
			WorldStateSyncer.SetWorldScanPaused(true);
			_worldScanPaused = true;
		}

		private void ResumeWorldScan()
		{
			if (!_worldScanPaused)
				return;
			WorldStateSyncer.SetWorldScanPaused(false);
			_worldScanPaused = false;
		}

		private bool HasPendingBulkPackets()
		{
			foreach (ulong clientId in _pendingClients)
			{
				if (MultiplayerSession.ConnectedPlayers.TryGetValue(clientId, out var player)
				    && PacketSender.PendingBulkCountForTests(player.Connection) > 0)
					return true;
			}
			return false;
		}

		private void InvalidateFenceDelivery()
		{
			unchecked { _fenceDeliveryToken++; }
			_fenceDeliveryCompleted = false;
		}

		internal static bool IsSpecificFenceDeliveryPending(bool completed)
			=> !completed;

		private bool SendFenceWithCompletion(IPacket fence, ProbeState waitingState)
		{
			if (_pendingClients.Count != 1
			    || !MultiplayerSession.ConnectedPlayers.TryGetValue(
				    _pendingClients.Single(), out MultiplayerPlayer player)
			    || player.Connection == null)
			{
				Abort("fence connection became unavailable");
				return false;
			}
			_state = waitingState;
			_stateStartedAt = Time.realtimeSinceStartup;
			_fenceDeliveryCompleted = false;
			int token = unchecked(++_fenceDeliveryToken);
			int runId = _runId;
			int sampleId = _sampleId;
			bool sent = PacketSender.SendReliableWithCompletion(
				player.Connection, fence,
				success => CompleteFenceDelivery(token, runId, sampleId, waitingState, success));
			if (!sent)
				CompleteFenceDelivery(token, runId, sampleId, waitingState, false);
			return sent;
		}

		private void CompleteFenceDelivery(
			int token, int runId, int sampleId, ProbeState waitingState, bool success)
		{
			if (!_running || token != _fenceDeliveryToken || runId != _runId
			    || sampleId != _sampleId || _state != waitingState)
				return;
			_fenceDeliveryCompleted = true;
			if (!success)
			{
				Abort("fence transport delivery failed");
				return;
			}
			_stateStartedAt = Time.realtimeSinceStartup;
		}

		private bool WaitForSpecificFenceDelivery(float elapsed)
		{
			if (!IsSpecificFenceDeliveryPending(_fenceDeliveryCompleted))
				return false;
			if (elapsed >= TransportDrainTimeoutSeconds)
				Abort("fence transport delivery timeout");
			return true;
		}
	}
}
#endif
