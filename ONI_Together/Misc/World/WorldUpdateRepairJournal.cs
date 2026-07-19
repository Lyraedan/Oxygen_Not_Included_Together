using System;
using System.Collections.Generic;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.Misc.World
{
	internal sealed class WorldUpdateRepairJournal
	{
		internal const int DefaultMaxEntries = WorldUpdateRepairObservability.MaxPendingPackets;
		internal const int DefaultMaxUpdates = WorldUpdateRepairObservability.MaxPendingUpdates;
		internal const float DefaultReplayIntervalSeconds = 1f;

		private sealed class Entry
		{
			internal WorldUpdatePacket Packet;
			internal readonly SortedSet<ulong> PendingClients = new();
			internal readonly Dictionary<ulong, float> LastReplayAt = new();
		}

		private readonly object _gate = new();
		private readonly int _maxEntries;
		private readonly int _maxUpdates;
		private readonly float _replayIntervalSeconds;
		private readonly SortedDictionary<long, Entry> _entries = new();
		private readonly Dictionary<ulong, long> _appliedThrough = new();
		private int _pendingUpdates;
		private long _retransmitCount;
		private long _replayCursorSequence;
		private ulong _replayCursorClient;
		private bool _backpressured;

		internal WorldUpdateRepairJournal(
			int maxEntries = DefaultMaxEntries,
			int maxUpdates = DefaultMaxUpdates,
			float replayIntervalSeconds = DefaultReplayIntervalSeconds)
		{
			if (maxEntries <= 0 || maxUpdates <= 0 || replayIntervalSeconds <= 0f
			    || float.IsNaN(replayIntervalSeconds) || float.IsInfinity(replayIntervalSeconds))
				throw new ArgumentOutOfRangeException();
			_maxEntries = maxEntries;
			_maxUpdates = maxUpdates;
			_replayIntervalSeconds = replayIntervalSeconds;
		}

		internal bool TryRecordNext(
			WorldUpdatePacket packet, IEnumerable<ulong> clientIds, float now)
		{
			if (packet == null || !packet.IsBackgroundRepair || packet.RepairSequence != 0)
				return false;
			var clients = new SortedSet<ulong>(clientIds ?? Array.Empty<ulong>());
			clients.Remove(0);
			lock (_gate)
			{
				if (clients.Count != 0 && !HasCapacityLocked(packet.Updates.Count))
				{
					_backpressured = true;
					return false;
				}
				packet.RepairSequence = WorldUpdatePacket.NextHostRepairDispatchSequence();
				if (clients.Count == 0)
					return true;
				var entry = new Entry { Packet = Clone(packet) };
				foreach (ulong clientId in clients)
				{
					if (_appliedThrough.TryGetValue(clientId, out long applied)
					    && applied >= packet.RepairSequence)
						continue;
					entry.PendingClients.Add(clientId);
					entry.LastReplayAt[clientId] = now;
				}
				if (entry.PendingClients.Count != 0)
				{
					_entries.Add(packet.RepairSequence, entry);
					_pendingUpdates += packet.Updates.Count;
				}
				return true;
			}
		}

		internal bool AcceptAppliedAck(ulong clientId, long appliedThrough)
		{
			if (clientId == 0 || appliedThrough <= 0
			    || appliedThrough > WorldUpdatePacket.CurrentHostRepairDispatchSequence)
				return false;
			lock (_gate)
			{
				_appliedThrough.TryGetValue(clientId, out long previous);
				if (appliedThrough < previous)
					return false;
				_appliedThrough[clientId] = appliedThrough;
				var completed = new List<long>();
				foreach (var pair in _entries)
				{
					if (pair.Key <= appliedThrough)
						pair.Value.PendingClients.Remove(clientId);
					if (pair.Value.PendingClients.Count == 0)
						completed.Add(pair.Key);
				}
				foreach (long sequence in completed)
					RemoveLocked(sequence);
				_backpressured = !HasCapacityLocked(1);
				return true;
			}
		}

		internal int DropClient(ulong clientId)
		{
			if (clientId == 0)
				return 0;
			lock (_gate)
			{
				_appliedThrough.Remove(clientId);
				var completed = new List<long>();
				foreach (var pair in _entries)
				{
					pair.Value.PendingClients.Remove(clientId);
					pair.Value.LastReplayAt.Remove(clientId);
					if (pair.Value.PendingClients.Count == 0)
						completed.Add(pair.Key);
				}
				foreach (long sequence in completed)
					RemoveLocked(sequence);
				_backpressured = !HasCapacityLocked(1);
				return completed.Count;
			}
		}

		internal bool ReplayPendingThrough(
			long sequenceCut, Func<ulong, WorldUpdatePacket, bool> send)
		{
			if (sequenceCut < 0 || send == null)
				return false;
			List<(ulong ClientId, WorldUpdatePacket Packet)> plan = SnapshotThrough(sequenceCut);
			foreach (var item in plan)
			{
				if (!send(item.ClientId, item.Packet))
					return false;
				lock (_gate)
					_retransmitCount++;
			}
			return true;
		}

		internal bool ReplayOneDue(
			float now, Func<ulong, WorldUpdatePacket, bool> send)
		{
			if (send == null || float.IsNaN(now) || float.IsInfinity(now))
				return false;
			ulong clientId = 0;
			WorldUpdatePacket packet = null;
			lock (_gate)
			{
				if (!TrySelectDueLocked(now, afterCursor: true, out clientId, out packet))
					TrySelectDueLocked(now, afterCursor: false, out clientId, out packet);
			}
			if (packet == null || !send(clientId, packet))
				return false;
			lock (_gate)
				_retransmitCount++;
			return true;
		}

		internal void Reset()
		{
			lock (_gate)
			{
				_entries.Clear();
				_appliedThrough.Clear();
				_pendingUpdates = 0;
				_retransmitCount = 0;
				_replayCursorSequence = 0;
				_replayCursorClient = 0;
				_backpressured = false;
			}
		}

		internal int PendingEntryCount
		{
			get { lock (_gate) return _entries.Count; }
		}

		internal int PendingUpdateCount
		{
			get { lock (_gate) return _pendingUpdates; }
		}

		internal long RetransmitCount
		{
			get { lock (_gate) return _retransmitCount; }
		}

		internal bool IsBackpressured
		{
			get { lock (_gate) return _backpressured; }
		}

		private List<(ulong ClientId, WorldUpdatePacket Packet)> SnapshotThrough(long cut)
		{
			var plan = new List<(ulong, WorldUpdatePacket)>();
			lock (_gate)
			{
				foreach (var pair in _entries)
				{
					if (pair.Key > cut)
						break;
					foreach (ulong clientId in pair.Value.PendingClients)
						plan.Add((clientId, pair.Value.Packet));
				}
			}
			return plan;
		}

		private bool TrySelectDueLocked(
			float now, bool afterCursor,
			out ulong clientId, out WorldUpdatePacket packet)
		{
			foreach (var pair in _entries)
			{
				foreach (ulong pendingClient in pair.Value.PendingClients)
				{
					bool isAfter = pair.Key > _replayCursorSequence
					               || pair.Key == _replayCursorSequence
					               && pendingClient > _replayCursorClient;
					if (isAfter != afterCursor
					    || now - pair.Value.LastReplayAt[pendingClient] < _replayIntervalSeconds)
						continue;
					pair.Value.LastReplayAt[pendingClient] = now;
					_replayCursorSequence = pair.Key;
					_replayCursorClient = pendingClient;
					clientId = pendingClient;
					packet = pair.Value.Packet;
					return true;
				}
			}
			clientId = 0;
			packet = null;
			return false;
		}

		private bool HasCapacityLocked(int updateCount)
			=> updateCount >= 0 && _entries.Count < _maxEntries
			   && updateCount <= _maxUpdates - _pendingUpdates;

		private void RemoveLocked(long sequence)
		{
			_pendingUpdates -= _entries[sequence].Packet.Updates.Count;
			_entries.Remove(sequence);
		}

		private static WorldUpdatePacket Clone(WorldUpdatePacket source)
		{
			return new WorldUpdatePacket
			{
				Revision = source.Revision,
				Sequence = source.Sequence,
				ForegroundCut = source.ForegroundCut,
				RepairSequence = source.RepairSequence,
				Updates = new List<WorldUpdatePacket.CellUpdate>(source.Updates),
			};
		}
	}

	internal static class WorldUpdateRepairObservability
	{
		private sealed class Observation
		{
			internal long Revision;
			internal List<WorldUpdatePacket.CellUpdate> Updates;
		}

		internal const int MaxPendingPackets = 128;
		internal const int MaxPendingUpdates = 65536;
		private static readonly object Gate = new();
		private static readonly SortedDictionary<long, Observation> Pending = new();
		private static int _pendingUpdates;
		private static bool _workScheduled;
		private static long _epoch;
		private static long _ackTarget;
		private static long _lastAckSent;

		internal static bool Track(
			WorldUpdatePacket packet, IEnumerable<WorldUpdatePacket.CellUpdate> updates)
		{
			if (packet == null || !packet.IsBackgroundRepair || packet.RepairSequence <= 0)
				return false;
			var accepted = new List<WorldUpdatePacket.CellUpdate>(
				updates ?? Array.Empty<WorldUpdatePacket.CellUpdate>());
			if (accepted.Count == 0)
				return false;
			lock (Gate)
			{
				if (packet.RepairSequence <= WorldUpdatePacket.ClientResolvedRepairSequence)
					return true;
				if (Pending.ContainsKey(packet.RepairSequence))
					return true;
				if (Pending.Count >= MaxPendingPackets
				         || accepted.Count > MaxPendingUpdates - _pendingUpdates)
					return false;
				Pending.Add(packet.RepairSequence, new Observation
				{
					Revision = packet.Revision,
					Updates = accepted,
				});
				_pendingUpdates += accepted.Count;
			}
			EnsureWorkScheduled();
			return true;
		}

		internal static int ObserveForTests(
			Func<WorldUpdatePacket.CellUpdate, bool> matches,
			Func<int, long> currentCellRevision)
		{
			if (matches == null || currentCellRevision == null)
				throw new ArgumentNullException();
			var complete = new List<long>();
			lock (Gate)
			{
				foreach (var pair in Pending)
				{
					bool observed = true;
					foreach (WorldUpdatePacket.CellUpdate update in pair.Value.Updates)
					{
						if (currentCellRevision(update.Cell) > pair.Value.Revision || matches(update))
							continue;
						observed = false;
						break;
					}
					if (observed)
						complete.Add(pair.Key);
				}
				foreach (long sequence in complete)
				{
					_pendingUpdates -= Pending[sequence].Updates.Count;
					Pending.Remove(sequence);
				}
			}
			foreach (long sequence in complete)
				WorldUpdatePacket.ResolveRepairSequence(sequence);
			return complete.Count;
		}

		internal static void NotifyResolved(long appliedThrough)
		{
			lock (Gate)
			{
				var completed = new List<long>();
				foreach (var pair in Pending)
				{
					if (pair.Key > appliedThrough)
						break;
					completed.Add(pair.Key);
				}
				foreach (long sequence in completed)
				{
					_pendingUpdates -= Pending[sequence].Updates.Count;
					Pending.Remove(sequence);
				}
				_ackTarget = Math.Max(_ackTarget, appliedThrough);
			}
			EnsureWorkScheduled();
		}

		internal static void SetBaseline(long appliedThrough)
		{
			lock (Gate)
			{
				Pending.Clear();
				_pendingUpdates = 0;
				_epoch++;
				_workScheduled = false;
				_ackTarget = appliedThrough;
				_lastAckSent = 0;
			}
			EnsureWorkScheduled();
		}

		internal static void Reset()
		{
			lock (Gate)
			{
				Pending.Clear();
				_pendingUpdates = 0;
				_epoch++;
				_workScheduled = false;
				_ackTarget = 0;
				_lastAckSent = 0;
			}
		}

		internal static int PendingCount
		{
			get { lock (Gate) return Pending.Count; }
		}

		private static void EnsureWorkScheduled()
		{
			GameScheduler scheduler = GameScheduler.Instance;
			if (scheduler == null)
				return;
			long epoch;
			lock (Gate)
			{
				if (_workScheduled)
					return;
				_workScheduled = true;
				epoch = _epoch;
			}
			scheduler.ScheduleNextFrame("ONI Together world repair observation",
				_ => RunScheduled(epoch));
		}

		private static void RunScheduled(long epoch)
		{
			lock (Gate)
			{
				if (epoch != _epoch)
					return;
				_workScheduled = false;
			}
			ObserveForTests(GridMatches, WorldUpdatePacket.GetClientCellRevision);
			TrySendAck();
			bool hasPending;
			lock (Gate)
				hasPending = Pending.Count != 0
				             || MultiplayerSession.IsClient && _ackTarget > _lastAckSent;
			if (hasPending)
				EnsureWorkScheduled();
		}

		private static void TrySendAck()
		{
			long target;
			lock (Gate)
				target = _ackTarget;
			if (!MultiplayerSession.IsClient || target <= 0)
				return;
			lock (Gate)
			{
				if (target <= _lastAckSent)
					return;
			}
			if (!PacketSender.SendToHost(
				    new WorldRepairAckPacket { AppliedThrough = target },
				    PacketSendMode.ReliableImmediate))
				return;
			lock (Gate)
				_lastAckSent = Math.Max(_lastAckSent, target);
		}

		private static bool GridMatches(WorldUpdatePacket.CellUpdate update)
		{
			if (!Grid.IsValidCell(update.Cell)
			    || Grid.ElementIdx[update.Cell] != update.ElementIdx
			    || !Grid.Mass[update.Cell].Equals(update.Mass)
			    || Grid.DiseaseIdx[update.Cell] != update.DiseaseIdx
			    || Grid.DiseaseCount[update.Cell] != update.DiseaseCount)
				return false;
			return update.Mass == 0f || Grid.Temperature[update.Cell].Equals(update.Temperature);
		}
	}
}
