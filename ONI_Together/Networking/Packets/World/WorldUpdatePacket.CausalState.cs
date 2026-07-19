using System.Collections.Generic;
using System.Threading;
using ONI_Together.Misc.World;

namespace ONI_Together.Networking.Packets.World
{
	public partial class WorldUpdatePacket
	{
		internal const int MaxPendingRepairPackets = 128;
		internal const int MaxPendingRepairUpdates = 65536;
		internal const int MaxResolvedRepairGaps = 16384;
		private static long _nextHostRevision;
		private static long _nextHostForegroundSequence;
		private static long _nextHostRepairDispatchSequence;
		private static long _clientSupersededRevision;
		private static long _clientForegroundSequence;
		private static long _clientResolvedRepairSequence;
		private static bool _clientForegroundInitialized;
		private static readonly Dictionary<int, long> ClientCellRevisions = new();
		private static readonly SortedDictionary<long, WorldUpdatePacket> PendingRepairs = new();
		private static readonly SortedSet<long> ResolvedRepairGaps = new();
		private static int _pendingRepairUpdates;
		private static readonly object ClientStateLock = new();

		internal static long NextHostRevision()
		{
			long revision = Interlocked.Increment(ref _nextHostRevision);
			if (revision > 0)
				return revision;
			Interlocked.Exchange(ref _nextHostRevision, 1);
			return 1;
		}

		internal static long CurrentHostRevision => Interlocked.Read(ref _nextHostRevision);

		internal static long NextHostForegroundSequence()
		{
			long sequence = Interlocked.Increment(ref _nextHostForegroundSequence);
			if (sequence > 0)
				return sequence;
			Interlocked.Exchange(ref _nextHostForegroundSequence, 1);
			return 1;
		}

		internal static long CurrentHostForegroundSequence
			=> Interlocked.Read(ref _nextHostForegroundSequence);

		internal static long NextHostRepairDispatchSequence()
		{
			long sequence = Interlocked.Increment(ref _nextHostRepairDispatchSequence);
			if (sequence > 0)
				return sequence;
			Interlocked.Exchange(ref _nextHostRepairDispatchSequence, 1);
			return 1;
		}

		internal static long CurrentHostRepairDispatchSequence
			=> Interlocked.Read(ref _nextHostRepairDispatchSequence);

		internal static long ClientSupersededRevision
			=> Interlocked.Read(ref _clientSupersededRevision);

		internal static void AdvanceClientSupersededRevision(long revision)
		{
			if (revision < 0)
				throw new System.ArgumentOutOfRangeException(nameof(revision));
			long current = Interlocked.Read(ref _clientSupersededRevision);
			while (revision > current)
			{
				long observed = Interlocked.CompareExchange(
					ref _clientSupersededRevision, revision, current);
				if (observed == current)
					break;
				current = observed;
			}
			DropSupersededRepairs(revision);
		}

		private static void DropSupersededRepairs(long revision)
		{
			var repairSequences = new List<long>();
			lock (ClientStateLock)
			{
				var superseded = new List<long>();
				foreach (var entry in PendingRepairs)
				{
					if (entry.Key <= revision)
						superseded.Add(entry.Key);
				}
				foreach (long key in superseded)
				{
					repairSequences.Add(PendingRepairs[key].RepairSequence);
					_pendingRepairUpdates -= PendingRepairs[key].Updates.Count;
					PendingRepairs.Remove(key);
				}
			}
			foreach (long repairSequence in repairSequences)
				ResolveRepairSequence(repairSequence);
		}

		internal static void ResetRevisionState()
		{
			Interlocked.Exchange(ref _nextHostRevision, 0);
			Interlocked.Exchange(ref _nextHostForegroundSequence, 0);
			Interlocked.Exchange(ref _nextHostRepairDispatchSequence, 0);
			Interlocked.Exchange(ref _clientSupersededRevision, 0);
			lock (ClientStateLock)
			{
				_clientForegroundSequence = 0;
				_clientResolvedRepairSequence = 0;
				_clientForegroundInitialized = false;
				ClientCellRevisions.Clear();
				PendingRepairs.Clear();
				ResolvedRepairGaps.Clear();
				_pendingRepairUpdates = 0;
			}
			WorldUpdateRepairObservability.Reset();
		}

		internal static ForegroundSequenceResult AcceptForegroundSequence(long sequence)
		{
			if (sequence <= 0)
				return ForegroundSequenceResult.Gap;
			lock (ClientStateLock)
			{
				if (!_clientForegroundInitialized)
				{
					_clientForegroundInitialized = true;
					_clientForegroundSequence = sequence;
					return ForegroundSequenceResult.Accepted;
				}
				if (sequence <= _clientForegroundSequence)
					return ForegroundSequenceResult.Superseded;
				if (sequence != _clientForegroundSequence + 1)
					return ForegroundSequenceResult.Gap;
				_clientForegroundSequence = sequence;
				return ForegroundSequenceResult.Accepted;
			}
		}

		internal static bool TryAcceptForegroundSequence(long sequence)
			=> AcceptForegroundSequence(sequence) == ForegroundSequenceResult.Accepted;

		internal static long CurrentClientForegroundSequence
		{
			get { lock (ClientStateLock) return _clientForegroundSequence; }
		}

		internal static void SetClientForegroundBaseline(long foregroundCut)
		{
			if (foregroundCut < 0)
				throw new System.ArgumentOutOfRangeException(nameof(foregroundCut));
			lock (ClientStateLock)
			{
				_clientForegroundSequence = foregroundCut;
				_clientForegroundInitialized = true;
				PendingRepairs.Clear();
				_pendingRepairUpdates = 0;
			}
		}

		internal static void SetClientRepairBaseline(long repairSequenceCut)
		{
			if (repairSequenceCut < 0)
				throw new System.ArgumentOutOfRangeException(nameof(repairSequenceCut));
			lock (ClientStateLock)
			{
				_clientResolvedRepairSequence = repairSequenceCut;
				ResolvedRepairGaps.RemoveWhere(sequence => sequence <= repairSequenceCut);
				while (ResolvedRepairGaps.Remove(_clientResolvedRepairSequence + 1))
					_clientResolvedRepairSequence++;
			}
			WorldUpdateRepairObservability.SetBaseline(repairSequenceCut);
		}

		internal static long ClientResolvedRepairSequence
		{
			get { lock (ClientStateLock) return _clientResolvedRepairSequence; }
		}

		internal static bool ResolveRepairSequence(long repairSequence)
		{
			long previous;
			long appliedThrough;
			bool accepted;
			lock (ClientStateLock)
			{
				previous = _clientResolvedRepairSequence;
				accepted = ResolveRepairSequenceLocked(repairSequence);
				appliedThrough = _clientResolvedRepairSequence;
			}
			if (appliedThrough > previous)
				WorldUpdateRepairObservability.NotifyResolved(appliedThrough);
			return accepted;
		}

		private static bool ResolveRepairSequenceLocked(long repairSequence)
		{
			if (repairSequence <= 0)
				return false;
			if (repairSequence <= _clientResolvedRepairSequence)
				return true;
			if (repairSequence == _clientResolvedRepairSequence + 1)
			{
				_clientResolvedRepairSequence = repairSequence;
				while (ResolvedRepairGaps.Remove(_clientResolvedRepairSequence + 1))
					_clientResolvedRepairSequence++;
				return true;
			}
			if (ResolvedRepairGaps.Count >= MaxResolvedRepairGaps)
				return false;
			ResolvedRepairGaps.Add(repairSequence);
			return true;
		}

		internal static bool ShouldDeferRepair(long foregroundCut)
		{
			if (foregroundCut < 0)
				return false;
			lock (ClientStateLock)
				return foregroundCut > 0 && (!_clientForegroundInitialized
				       || foregroundCut > _clientForegroundSequence);
		}

		internal static bool TryAcceptCellRevision(
			int cell, long revision, bool backgroundRepair)
		{
			if (cell < 0 || revision <= 0)
				return false;
			lock (ClientStateLock)
			{
				ClientCellRevisions.TryGetValue(cell, out long previous);
				if (backgroundRepair && revision < previous)
					return false;
				if (revision > previous)
					ClientCellRevisions[cell] = revision;
				return true;
			}
		}

		internal static long GetClientCellRevision(int cell)
		{
			lock (ClientStateLock)
				return ClientCellRevisions.TryGetValue(cell, out long revision) ? revision : 0;
		}

		internal static bool DeferRepair(WorldUpdatePacket packet)
		{
			if (packet == null || !packet.IsBackgroundRepair || packet.Revision <= 0
			    || packet.RepairSequence <= 0
			    || packet.Updates.Count > MaxPendingRepairUpdates)
				return false;
			lock (ClientStateLock)
			{
				if (PendingRepairs.ContainsKey(packet.Revision))
					return true;
				if (PendingRepairs.Count >= MaxPendingRepairPackets
				    || _pendingRepairUpdates > MaxPendingRepairUpdates - packet.Updates.Count)
					return false;
				PendingRepairs.Add(packet.Revision, packet);
				_pendingRepairUpdates += packet.Updates.Count;
				return true;
			}
		}

		internal static int PendingRepairPacketCount
		{
			get { lock (ClientStateLock) return PendingRepairs.Count; }
		}

		internal static int PendingRepairUpdateCount
		{
			get { lock (ClientStateLock) return _pendingRepairUpdates; }
		}

	}
}
