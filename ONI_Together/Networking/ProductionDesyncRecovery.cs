using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Misc.World;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Patches;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking
{
	internal enum ProductionRecoveryPhase
	{
		Idle,
		AwaitingPause,
		AwaitingDrain,
		AwaitingReports,
		RepairingGrid,
		AwaitingRepairDrain,
	}

	public enum ProductionRecoveryAction
	{
		Release,
		GridRepair,
		HardSync,
	}

	public static class ProductionDesyncRecovery
	{
		private const float FenceTimeoutSeconds = 30f;
		private const float GridRepairTimeoutSeconds = 300f;
		private const int GridCellsPerFrame = 4096;
		private static readonly HashSet<ulong> Clients = new();
		private static readonly HashSet<ulong> Awaiting = new();
		private static ProductionRecoveryPhase _phase;
		private static int _nextProbeId;
		private static int _probeId;
		private static byte _attempt;
		private static long _repairSequenceCut;
		private static float _phaseStartedAt;
		private static int _gridRepairCell;
		private static bool _localRepairAttempted;
		private static SpeedChangePacket.SpeedState _previousSpeed;
		private static ProductionStateHashes _hostHashes;
		private static ProductionDesyncDomain _mismatch;
		private static int _clientProbeId;
		private static ProductionDesyncFencePacket _clientFence;
		internal static bool IsActive => _phase != ProductionRecoveryPhase.Idle;

		public static bool TryBeginCycleProbe(int cycle)
		{
			if (!CanBegin(cycle)) return false;
			Clients.Clear();
			Clients.UnionWith(ReadyManager.GetReadyClientIds());
			if (Clients.Count == 0) return false;
			_probeId = NextProbeId();
			_attempt = 0;
			_localRepairAttempted = false;
			_mismatch = ProductionDesyncDomain.None;
			_previousSpeed = CurrentSpeed();
			SetLocalSpeed(SpeedChangePacket.SpeedState.Paused);
			SpeedChangePacket.SubmitLocalChange(SpeedChangePacket.SpeedState.Paused);
			Awaiting.Clear();
			Awaiting.UnionWith(Clients);
			StartPhase(ProductionRecoveryPhase.AwaitingPause);
			foreach (ulong clientId in Clients)
				if (!PacketSender.SendToPlayer(clientId,
					new ProductionDesyncPausePacket { ProbeId = _probeId }))
					return Escalate("pause fence send failed");
			DebugConsole.Log($"[ProductionDesync] Probe {_probeId} started at cycle {cycle}");
			return true;
		}

		private static bool CanBegin(int cycle)
		{
#if DEBUG
			if (!CanStartAgainstDebugProbe(SoakStateHashProbe.IsRunning)) return false;
#endif
			return cycle >= 0 && MultiplayerSession.IsHostInSession
			       && GameClock.Instance?.GetCycle() == cycle
			       && _phase == ProductionRecoveryPhase.Idle
			       && !GameServerHardSync.IsHardSyncInProgress
			       && !ReadyManager.HasActiveSyncBarrier
			       && SpeedControlScreen.Instance != null;
		}

		internal static bool CanStartAgainstDebugProbe(bool debugProbeRunning)
			=> !debugProbeRunning;

		internal static void Update()
		{
			if (!MultiplayerSession.InSession) return;
			if (MultiplayerSession.IsClient)
			{
				UpdateClient();
				return;
			}
			if (_phase == ProductionRecoveryPhase.Idle) return;
			SetLocalSpeed(SpeedChangePacket.SpeedState.Paused);
			float timeout = _phase is ProductionRecoveryPhase.RepairingGrid
				or ProductionRecoveryPhase.AwaitingRepairDrain
				? GridRepairTimeoutSeconds : FenceTimeoutSeconds;
			if (Time.realtimeSinceStartup - _phaseStartedAt >= timeout)
			{
				Escalate($"{_phase} timeout");
				return;
			}
			PumpHostPhase();
		}

		private static void PumpHostPhase()
		{
			switch (_phase)
			{
				case ProductionRecoveryPhase.AwaitingDrain:
					TryBeginHashFence();
					break;
				case ProductionRecoveryPhase.RepairingGrid:
					PumpGridRepair();
					break;
				case ProductionRecoveryPhase.AwaitingRepairDrain:
					TryBeginRecheck();
					break;
			}
		}

		internal static void ReceivePauseAck(
			ProductionDesyncPauseAckPacket packet, DispatchContext context)
		{
			if (_phase != ProductionRecoveryPhase.AwaitingPause || context.SenderIsHost
			    || packet.ProbeId != _probeId || !Awaiting.Remove(context.SenderId)) return;
			if (Awaiting.Count == 0) StartPhase(ProductionRecoveryPhase.AwaitingDrain);
		}

		private static void TryBeginHashFence()
		{
			WorldUpdateBatcher.Flush();
			PacketSender.DispatchPendingBulkPackets();
			if (WorldUpdateBatcher.HasPendingDispatch
			    || !WorldUpdateBatcher.TryFreezeRepairDispatch(out _repairSequenceCut)) return;
			try
			{
				_hostHashes = ProductionStateHash.CaptureCurrent();
			}
			catch (Exception ex)
			{
				Escalate("hash capture failed: " + ex.Message);
				return;
			}
			Awaiting.Clear();
			Awaiting.UnionWith(Clients);
			StartPhase(ProductionRecoveryPhase.AwaitingReports);
			var fence = new ProductionDesyncFencePacket
				{ ProbeId = _probeId, Attempt = _attempt, RepairSequenceCut = _repairSequenceCut };
			foreach (ulong clientId in Clients)
				if (!PacketSender.SendToPlayer(clientId, fence))
				{
					Escalate("hash fence send failed");
					return;
				}
		}

		internal static void ReceiveReport(
			ProductionDesyncReportPacket packet, DispatchContext context)
		{
			if (_phase != ProductionRecoveryPhase.AwaitingReports || context.SenderIsHost
			    || packet.ProbeId != _probeId || packet.Attempt != _attempt
			    || packet.RepairSequenceCut != _repairSequenceCut
			    || !Awaiting.Remove(context.SenderId)) return;
			ProductionDesyncDomain mismatch = _hostHashes.DifferentDomains(packet.Hashes);
			_mismatch |= mismatch;
			DebugConsole.Log($"[ProductionDesync] Probe {_probeId}/{_attempt} client "
			                 + $"{context.SenderId} mismatch={mismatch}");
			if (Awaiting.Count == 0) CompleteComparison();
		}

		private static void CompleteComparison()
		{
			if (!WorldUpdateBatcher.IsFrozenCheckpointValid())
			{
				Escalate("host checkpoint mutated before comparison");
				return;
			}
			ProductionStateHashes current;
			try { current = ProductionStateHash.CaptureCurrent(); }
			catch (Exception ex)
			{
				Escalate("comparison hash capture failed: " + ex.Message);
				return;
			}
			if (_hostHashes.DifferentDomains(current) != ProductionDesyncDomain.None)
			{
				Escalate("host state mutated during comparison");
				return;
			}
			switch (SelectRecoveryAction(_mismatch, _localRepairAttempted))
			{
				case ProductionRecoveryAction.Release: ReleaseMatched(); break;
				case ProductionRecoveryAction.GridRepair: BeginGridRepair(); break;
				default: Escalate("domain mismatch: " + _mismatch); break;
			}
		}

		public static ProductionRecoveryAction SelectRecoveryAction(
			ProductionDesyncDomain mismatch, bool localRepairAttempted)
		{
			if (mismatch == ProductionDesyncDomain.None) return ProductionRecoveryAction.Release;
			return mismatch == ProductionDesyncDomain.Grid && !localRepairAttempted
				? ProductionRecoveryAction.GridRepair : ProductionRecoveryAction.HardSync;
		}

		private static void BeginGridRepair()
		{
			WorldUpdateBatcher.ResumeRepairDispatch();
			_localRepairAttempted = true;
			_gridRepairCell = 0;
			StartPhase(ProductionRecoveryPhase.RepairingGrid);
			DebugConsole.LogWarning($"[ProductionDesync] Probe {_probeId} starting authoritative grid repair");
		}

		private static void PumpGridRepair()
		{
			int queued = 0;
			while (_gridRepairCell < Grid.CellCount && queued < GridCellsPerFrame)
			{
				int cell = _gridRepairCell++;
				if (!Grid.IsValidCell(cell)) continue;
				if (!WorldUpdateBatcher.Queue(CaptureCell(cell)))
				{
					Escalate("authoritative grid repair queue failed");
					return;
				}
				queued++;
			}
			WorldUpdateBatcher.Flush();
			if (_gridRepairCell >= Grid.CellCount)
				StartPhase(ProductionRecoveryPhase.AwaitingRepairDrain);
		}

		private static WorldUpdatePacket.CellUpdate CaptureCell(int cell)
			=> new()
			{
				Cell = cell,
				ElementIdx = Grid.ElementIdx[cell],
				Mass = Grid.Mass[cell],
				Temperature = Grid.Temperature[cell],
				DiseaseIdx = Grid.DiseaseIdx[cell],
				DiseaseCount = Grid.DiseaseCount[cell],
				ReplaceType = SimMessages.ReplaceType.Replace,
			};

		private static void TryBeginRecheck()
		{
			WorldUpdateBatcher.Flush();
			if (WorldUpdateBatcher.HasPendingDispatch) return;
			_attempt = 1;
			_mismatch = ProductionDesyncDomain.None;
			StartPhase(ProductionRecoveryPhase.AwaitingDrain);
		}

		private static void ReleaseMatched()
		{
			WorldUpdateBatcher.ResumeRepairDispatch();
			var release = new ProductionDesyncReleasePacket { ProbeId = _probeId };
			foreach (ulong clientId in Clients)
				if (!PacketSender.SendToPlayer(clientId, release))
				{
					Escalate("release send failed");
					return;
				}
			SpeedChangePacket.SpeedState speed = _previousSpeed;
			ResetHostState();
			SetLocalSpeed(speed);
			SpeedChangePacket.SubmitLocalChange(speed);
			DebugConsole.Log("[ProductionDesync] Causal checkpoint matched");
		}

		private static bool Escalate(string reason)
		{
			DebugConsole.LogWarning($"[ProductionDesync] Escalating to fresh hard sync: {reason}");
			CancelForHardSync();
			GameServerHardSync.PerformHardSync(false);
			return false;
		}

		internal static void CancelForHardSync()
		{
			WorldUpdateBatcher.ResumeRepairDispatch();
			ResetHostState();
		}

		public static void ResetSessionState()
		{
			WorldUpdateBatcher.ResumeRepairDispatch();
			ResetHostState();
			_clientProbeId = 0;
			_clientFence = null;
		}

		private static void ResetHostState()
		{
			_phase = ProductionRecoveryPhase.Idle;
			Clients.Clear();
			Awaiting.Clear();
			_probeId = 0;
			_hostHashes = null;
			_mismatch = ProductionDesyncDomain.None;
		}

		internal static void ReceivePause(ProductionDesyncPausePacket packet)
		{
			if (!MultiplayerSession.IsClient || packet.ProbeId <= _clientProbeId) return;
			_clientProbeId = packet.ProbeId;
			_clientFence = null;
			SetLocalSpeed(SpeedChangePacket.SpeedState.Paused);
			PacketSender.SendToHost(new ProductionDesyncPauseAckPacket { ProbeId = packet.ProbeId });
		}

		internal static void ReceiveFence(ProductionDesyncFencePacket packet)
		{
			if (MultiplayerSession.IsClient && packet.ProbeId == _clientProbeId)
				_clientFence = packet;
		}

		private static void UpdateClient()
		{
			if (_clientProbeId == 0) return;
			SetLocalSpeed(SpeedChangePacket.SpeedState.Paused);
			ProductionDesyncFencePacket fence = _clientFence;
			if (fence == null || WorldUpdatePacket.ClientResolvedRepairSequence
			    < fence.RepairSequenceCut) return;
			ProductionStateHashes hashes;
			try { hashes = ProductionStateHash.CaptureCurrent(); }
			catch (Exception ex)
			{
				DebugConsole.LogWarning("[ProductionDesync] Client hash capture failed: " + ex.Message);
				_clientFence = null;
				return;
			}
			if (!PacketSender.SendToHost(new ProductionDesyncReportPacket
			    {
				    ProbeId = fence.ProbeId,
				    Attempt = fence.Attempt,
				    RepairSequenceCut = fence.RepairSequenceCut,
				    Hashes = hashes,
			    })) return;
			_clientFence = null;
		}

		internal static void ReleaseClient(int probeId)
		{
			if (probeId != _clientProbeId) return;
			_clientProbeId = 0;
			_clientFence = null;
		}

		internal static void ReleaseClientForHardSync()
		{
			_clientProbeId = 0;
			_clientFence = null;
		}

		private static void StartPhase(ProductionRecoveryPhase phase)
		{
			_phase = phase;
			_phaseStartedAt = Time.realtimeSinceStartup;
		}

		private static int NextProbeId()
		{
			_nextProbeId = _nextProbeId == int.MaxValue ? 1 : _nextProbeId + 1;
			return _nextProbeId;
		}

		private static SpeedChangePacket.SpeedState CurrentSpeed()
			=> SpeedControlScreen.Instance.IsPaused
				? SpeedChangePacket.SpeedState.Paused
				: (SpeedChangePacket.SpeedState)SpeedControlScreen.Instance.GetSpeed();

		private static void SetLocalSpeed(SpeedChangePacket.SpeedState speed)
		{
			SpeedControlScreen control = SpeedControlScreen.Instance;
			if (control == null) return;
			SpeedControlScreen_SendSpeedPacketPatch.IsSyncing = true;
			try
			{
				if (speed == SpeedChangePacket.SpeedState.Paused)
				{
					if (!control.IsPaused) control.Pause(false);
				}
				else
				{
					if (control.IsPaused) control.Unpause(false);
					control.SetSpeed((int)speed);
				}
			}
			finally { SpeedControlScreen_SendSpeedPacketPatch.IsSyncing = false; }
		}
	}

	[HarmonyPatch(typeof(Game), "Update")]
	internal static class ProductionDesyncRecoveryUpdatePatch
	{
		[HarmonyPostfix]
		private static void Postfix() => ProductionDesyncRecovery.Update();
	}
}

namespace ONI_Together.Networking.Packets.World
{
	public sealed class ProductionDesyncPausePacket : IPacket, IHostOnlyPacket
	{
		public int ProbeId;
		public void Serialize(BinaryWriter writer) { Validate(); writer.Write(ProbeId); }
		public void Deserialize(BinaryReader reader) { ProbeId = reader.ReadInt32(); Validate(); }
		public void OnDispatched() => ProductionDesyncRecovery.ReceivePause(this);
		private void Validate() { if (ProbeId <= 0) throw new InvalidDataException("Invalid desync probe"); }
	}

	public sealed class ProductionDesyncPauseAckPacket : IPacket
	{
		public int ProbeId;
		public void Serialize(BinaryWriter writer) { Validate(); writer.Write(ProbeId); }
		public void Deserialize(BinaryReader reader) { ProbeId = reader.ReadInt32(); Validate(); }
		public void OnDispatched() => ProductionDesyncRecovery.ReceivePauseAck(this, PacketHandler.CurrentContext);
		private void Validate() { if (ProbeId <= 0) throw new InvalidDataException("Invalid desync pause ACK"); }
	}

	public sealed class ProductionDesyncFencePacket : IPacket, IHostOnlyPacket
	{
		public int ProbeId;
		public byte Attempt;
		public long RepairSequenceCut;
		public void Serialize(BinaryWriter writer) { Validate(); writer.Write(ProbeId); writer.Write(Attempt); writer.Write(RepairSequenceCut); }
		public void Deserialize(BinaryReader reader) { ProbeId = reader.ReadInt32(); Attempt = reader.ReadByte(); RepairSequenceCut = reader.ReadInt64(); Validate(); }
		public void OnDispatched() => ProductionDesyncRecovery.ReceiveFence(this);
		private void Validate() { if (ProbeId <= 0 || Attempt > 1 || RepairSequenceCut < 0) throw new InvalidDataException("Invalid desync fence"); }
	}

	public sealed class ProductionDesyncReportPacket : IPacket
	{
		public int ProbeId;
		public byte Attempt;
		public long RepairSequenceCut;
		public ProductionStateHashes Hashes = new();
		public void Serialize(BinaryWriter writer) { Validate(); writer.Write(ProbeId); writer.Write(Attempt); writer.Write(RepairSequenceCut); Hashes.Serialize(writer); }
		public void Deserialize(BinaryReader reader) { ProbeId = reader.ReadInt32(); Attempt = reader.ReadByte(); RepairSequenceCut = reader.ReadInt64(); Hashes = ProductionStateHashes.Deserialize(reader); Validate(); }
		public void OnDispatched() => ProductionDesyncRecovery.ReceiveReport(this, PacketHandler.CurrentContext);
		private void Validate() { if (ProbeId <= 0 || Attempt > 1 || RepairSequenceCut < 0 || Hashes == null) throw new InvalidDataException("Invalid desync report"); Hashes.Validate(); }
	}

	public sealed class ProductionDesyncReleasePacket : IPacket, IHostOnlyPacket
	{
		public int ProbeId;
		public void Serialize(BinaryWriter writer) { Validate(); writer.Write(ProbeId); }
		public void Deserialize(BinaryReader reader) { ProbeId = reader.ReadInt32(); Validate(); }
		public void OnDispatched() => ProductionDesyncRecovery.ReleaseClient(ProbeId);
		private void Validate() { if (ProbeId <= 0) throw new InvalidDataException("Invalid desync release"); }
	}
}
