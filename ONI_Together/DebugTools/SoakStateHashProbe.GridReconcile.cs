#if DEBUG
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ONI_Together.Misc.World;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.World;
using UnityEngine;

namespace ONI_Together.DebugTools
{
	internal sealed partial class SoakStateHashProbe
	{
		private const int ClientGridApplyPollLimit = 300;
		private static long _nextGridReconcileGeneration;
		private static SoakGridReconcileSession _clientGridSession;
		private static SoakGridMarker _clientGridMarker;
		private static bool _clientGridAcksSent;
		private static bool _clientGridAbortSent;
		private static bool _clientGridTerminal;
		private IReadOnlyList<SoakGridReconcileChunkPacket> _gridChunks;
		private SoakGridChunkSendCursor _gridSendCursor;
		private float _nextGridChunkSendAt;
		private SoakGridAckTracker _gridAckTracker;
		private SoakGridProof _hostGridProof;

		private void BeginGridReconcile()
		{
			if (!PopulatePendingClients())
			{
				Abort("remote client set changed before grid reconcile");
				return;
			}
			try
			{
				WorldUpdateBatcher.Flush();
				long worldUpdateCut = WorldUpdatePacket.CurrentHostRevision;
				List<SoakGridCell> cells = SoakGridRuntime.CaptureCells();
				long generation = NextGridReconcileGeneration();
				_gridChunks = SoakGridChunkPlanner.Plan(
					new SoakGridMarker(
						_runId, _sampleId, generation, worldUpdateCut), cells);
				_gridSendCursor = new SoakGridChunkSendCursor(_gridChunks);
				_gridAckTracker = new SoakGridAckTracker(_pendingClients, _gridChunks);
				_hostGridProof = SoakGridProof.FromCells(cells);
				_state = ProbeState.WaitingForGridReconcile;
				_stateStartedAt = Time.realtimeSinceStartup;
				_nextGridChunkSendAt = _stateStartedAt;
				DebugConsole.Log($"[SoakHash][GRID_RECONCILE] sample={_sampleId} " +
					$"generation={generation} worldCut={worldUpdateCut} " +
					$"records={cells.Count} chunks={_gridChunks.Count}");
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogWarning($"[SoakHash] Grid reconcile setup failed: {ex.Message}");
				Abort("grid reconcile setup failed");
			}
		}

		private void PumpHostGridReconcileSend()
		{
			if (_gridSendCursor == null || Time.realtimeSinceStartup < _nextGridChunkSendAt
			    || !_gridSendCursor.TryTakeNext(out SoakGridReconcileChunkPacket chunk))
				return;
			PacketSender.SendToAllClients(chunk, PacketSendMode.ReliableImmediate);
			_nextGridChunkSendAt = Time.realtimeSinceStartup + GridChunkSendIntervalSeconds;
			if (_gridSendCursor.IsComplete)
			{
				_stateStartedAt = Time.realtimeSinceStartup;
				DebugConsole.Log($"[SoakHash][GRID_SEND_COMPLETE] sample={_sampleId} " +
					$"chunks={_gridChunks.Count}");
			}
		}

		internal static void ReceiveGridReconcileAck(
			SoakGridReconcileAckPacket ack, DispatchContext context)
		{
			_instance?.AcceptGridReconcileAck(ack, context);
		}

		internal static void ReceiveGridReconcileAbort(
			SoakGridReconcileAbortPacket packet, DispatchContext context)
		{
			_instance?.AcceptGridReconcileAbort(packet, context);
		}

		private void AcceptGridReconcileAck(
			SoakGridReconcileAckPacket ack, DispatchContext context)
		{
			if (!_running || _state != ProbeState.WaitingForGridReconcile
			    || context.SenderIsHost || !_pendingClients.Contains(context.SenderId)
			    || _gridAckTracker == null)
				return;
			SoakGridAckResult result = _gridAckTracker.Accept(context.SenderId, ack);
			if (result != SoakGridAckResult.Complete)
				return;
			DebugConsole.Log($"[SoakHash][GRID_ACK] sample={_sampleId} clients={_pendingClients.Count}");
			_gridChunks = null;
			_gridSendCursor = null;
			_gridAckTracker = null;
			SendPostKeyframeFence();
		}

		private void AcceptGridReconcileAbort(
			SoakGridReconcileAbortPacket packet, DispatchContext context)
		{
			if (!_running || _state != ProbeState.WaitingForGridReconcile
			    || context.SenderIsHost || !_pendingClients.Contains(context.SenderId)
			    || _gridChunks == null || _gridChunks.Count == 0
			    || !_gridChunks[0].Marker.Equals(
				    new SoakGridMarker(
					    packet.RunId, packet.SampleId, packet.Generation,
					    packet.WorldUpdateCut)))
				return;
			Abort("client grid reconcile did not converge");
		}

		private static long NextGridReconcileGeneration()
		{
			long generation = Interlocked.Increment(ref _nextGridReconcileGeneration);
			if (generation > 0)
				return generation;
			Interlocked.Exchange(ref _nextGridReconcileGeneration, 1);
			return 1;
		}

		private bool HostGridProofMatches(SoakStateHashes hashes)
		{
			return _hostGridProof != null
			       && hashes.GridRecords == _hostGridProof.RecordCount
			       && hashes.Grid.SequenceEqual(_hostGridProof.Hash);
		}

		private void ResetHostGridReconcile()
		{
			_gridChunks = null;
			_gridSendCursor = null;
			_nextGridChunkSendAt = 0f;
			_gridAckTracker = null;
			_hostGridProof = null;
		}

		internal static void ReceiveGridReconcileChunk(SoakGridReconcileChunkPacket chunk)
		{
			if (!CanAcceptClientGridMarker(chunk.Marker))
				return;
			if (_clientGridSession == null)
			{
				var session = new SoakGridReconcileSession(ClientGridApplyPollLimit);
				if (session.Accept(chunk) != SoakGridAcceptResult.Accepted)
					return;
				_clientGridMarker = chunk.Marker;
				_clientGridSession = session;
				WorldUpdatePacket.AdvanceClientSupersededRevision(
					chunk.Marker.WorldUpdateCut);
				return;
			}
			if (!_clientGridMarker.Equals(chunk.Marker))
				return;
			SoakGridAcceptResult result = _clientGridSession.Accept(chunk);
			if (result == SoakGridAcceptResult.Duplicate && _clientGridAcksSent)
				SendClientGridAck();
		}

		private static bool CanAcceptClientGridMarker(SoakGridMarker marker)
		{
			return MultiplayerSession.IsClient && SpeedControlScreen.Instance?.IsPaused == true
			       && marker.RunId == _clientRunId
			       && marker.SampleId == _clientCompletedSampleId
			       && marker.Generation > 0;
		}

		private static void PumpClientGridReconcile()
		{
			if (_clientGridSession == null || _clientGridTerminal
			    || !CanAcceptClientGridMarker(_clientGridMarker))
				return;
			SoakGridPumpResult result;
			try
			{
				result = _clientGridSession.Pump(
					SoakGridRuntime.MatchesGrid,
					SoakGridRuntime.Apply,
					SoakGridRuntime.CaptureProof);
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogWarning($"[SoakHash] Client grid reconcile failed: {ex.Message}");
				SendClientGridAbort();
				return;
			}
			if (result == SoakGridPumpResult.Complete && !_clientGridAcksSent)
				SendClientGridAck();
			else if (result == SoakGridPumpResult.Aborted && !_clientGridAbortSent)
				SendClientGridAbort();
		}

		private static void SendClientGridAck()
		{
			if (!_clientGridSession.TryBuildAck(out SoakGridReconcileAckPacket ack))
				return;
			PacketSender.SendToHost(ack, PacketSendMode.ReliableImmediate);
			_clientGridAcksSent = true;
			DebugConsole.Log($"[SoakHash][GRID_PROOF_SENT] sample={_clientGridMarker.SampleId}");
		}

		private static void SendClientGridAbort()
		{
			PacketSender.SendToHost(new SoakGridReconcileAbortPacket
			{
				RunId = _clientGridMarker.RunId,
				SampleId = _clientGridMarker.SampleId,
				Generation = _clientGridMarker.Generation,
				WorldUpdateCut = _clientGridMarker.WorldUpdateCut,
				Reason = SoakGridAbortReason.ApplyDidNotConverge,
			}, PacketSendMode.ReliableImmediate);
			_clientGridAbortSent = true;
			_clientGridTerminal = true;
		}

		private static void ResetClientGridReconcile()
		{
			_clientGridSession = null;
			_clientGridMarker = default;
			_clientGridAcksSent = false;
			_clientGridAbortSent = false;
			_clientGridTerminal = false;
		}
	}

	internal static class SoakGridRuntime
	{
		internal static List<SoakGridCell> CaptureCells()
		{
			var cells = new List<SoakGridCell>(Grid.CellCount);
			for (int cell = 0; cell < Grid.CellCount; cell++)
			{
				if (Grid.IsValidCell(cell))
					cells.Add(CaptureCell(cell));
			}
			return cells;
		}

		internal static SoakGridProof CaptureProof()
			=> SoakGridProof.FromCells(CaptureCells());

		internal static bool MatchesGrid(SoakGridCell expected)
			=> Grid.IsValidCell(expected.Cell) && CaptureCell(expected.Cell).Equals(expected);

		internal static void Apply(SoakGridCell cell)
		{
			SimMessages.ModifyCell(
				cell.Cell,
				cell.ElementIdx,
				cell.Temperature,
				cell.Mass,
				cell.DiseaseIdx,
				cell.DiseaseCount,
				SimMessages.ReplaceType.Replace,
				false,
				-1);
		}

		private static SoakGridCell CaptureCell(int cell)
		{
			float mass = Grid.Mass[cell];
			return new SoakGridCell
			{
				Cell = cell,
				ElementIdx = Grid.ElementIdx[cell],
				Temperature = mass == 0f ? 0f : Grid.Temperature[cell],
				Mass = mass,
				DiseaseIdx = Grid.DiseaseIdx[cell],
				DiseaseCount = Grid.DiseaseCount[cell],
			};
		}
	}
}
#endif
