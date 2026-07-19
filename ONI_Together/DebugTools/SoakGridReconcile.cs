#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools
{
	internal readonly struct SoakGridMarker : IEquatable<SoakGridMarker>
	{
		internal SoakGridMarker(
			int runId, int sampleId, long generation, long worldUpdateCut)
		{
			RunId = runId;
			SampleId = sampleId;
			Generation = generation;
			WorldUpdateCut = worldUpdateCut;
		}

		internal int RunId { get; }
		internal int SampleId { get; }
		internal long Generation { get; }
		internal long WorldUpdateCut { get; }

		public bool Equals(SoakGridMarker other)
			=> RunId == other.RunId && SampleId == other.SampleId
			   && Generation == other.Generation && WorldUpdateCut == other.WorldUpdateCut;

		public override bool Equals(object obj)
			=> obj is SoakGridMarker other && Equals(other);

		public override int GetHashCode()
		{
			int hash = ((RunId * 397) ^ SampleId) * 397 ^ Generation.GetHashCode();
			return hash * 397 ^ WorldUpdateCut.GetHashCode();
		}
	}

	internal struct SoakGridCell : IEquatable<SoakGridCell>
	{
		internal int Cell;
		internal ushort ElementIdx;
		internal float Temperature;
		internal float Mass;
		internal byte DiseaseIdx;
		internal int DiseaseCount;

		public bool Equals(SoakGridCell other)
		{
			return Cell == other.Cell && ElementIdx == other.ElementIdx
			       && SameFloat(Temperature, other.Temperature)
			       && SameFloat(Mass, other.Mass)
			       && DiseaseIdx == other.DiseaseIdx
			       && DiseaseCount == other.DiseaseCount;
		}

		public override bool Equals(object obj)
			=> obj is SoakGridCell other && Equals(other);

		public override int GetHashCode() => Cell;

		private static bool SameFloat(float left, float right)
			=> SoakStateHash.NormalizeFloatBits(left) == SoakStateHash.NormalizeFloatBits(right);
	}

	internal sealed class SoakGridProof
	{
		private SoakGridProof(int recordCount, byte[] hash)
		{
			RecordCount = recordCount;
			Hash = hash;
		}

		internal int RecordCount { get; }
		internal byte[] Hash { get; }

		internal static SoakGridProof FromCells(IEnumerable<SoakGridCell> cells)
		{
			List<SoakGridCell> list = cells.ToList();
			return new SoakGridProof(list.Count, HashCells(list));
		}

		internal static byte[] HashCells(IEnumerable<SoakGridCell> cells)
		{
			return SoakStateHash.Compute(
				cells.Select(ToHashState),
				Enumerable.Empty<SoakEntityState>()).Grid;
		}

		private static SoakCellState ToHashState(SoakGridCell cell)
		{
			return new SoakCellState
			{
				Cell = cell.Cell,
				ElementIdx = cell.ElementIdx,
				Mass = SoakStateHash.NormalizeFloatBits(cell.Mass),
				Temperature = cell.Mass == 0f
					? 0
					: SoakStateHash.NormalizeFloatBits(cell.Temperature),
				DiseaseIdx = cell.DiseaseIdx,
				DiseaseCount = cell.DiseaseCount,
			};
		}
	}

	internal static class SoakGridChunkPlanner
	{
		internal static IReadOnlyList<SoakGridReconcileChunkPacket> Plan(
			SoakGridMarker marker,
			IEnumerable<SoakGridCell> source,
			int maxCellsPerChunk = SoakGridWire.MaxCellsPerChunk)
		{
			ValidatePlanInput(marker, source, maxCellsPerChunk);
			List<SoakGridCell> cells = source.OrderBy(cell => cell.Cell).ToList();
			if (cells.Count == 0 || cells.Count > SoakGridWire.MaxGridRecords)
				throw new ArgumentOutOfRangeException(nameof(source));
			if (cells.Select(cell => cell.Cell).Distinct().Count() != cells.Count)
				throw new ArgumentException("Grid reconcile cells must be unique", nameof(source));

			SoakGridProof fullProof = SoakGridProof.FromCells(cells);
			int totalChunks = (cells.Count + maxCellsPerChunk - 1) / maxCellsPerChunk;
			if (totalChunks > SoakGridWire.MaxChunks)
				throw new ArgumentOutOfRangeException(nameof(maxCellsPerChunk));
			var chunks = new List<SoakGridReconcileChunkPacket>(totalChunks);
			for (int index = 0; index < totalChunks; index++)
				chunks.Add(CreateChunk(marker, cells, fullProof, index, totalChunks, maxCellsPerChunk));
			return chunks;
		}

		private static void ValidatePlanInput(
			SoakGridMarker marker, IEnumerable<SoakGridCell> source, int maxCellsPerChunk)
		{
			if (marker.RunId <= 0 || marker.SampleId <= 0 || marker.Generation <= 0
			    || marker.WorldUpdateCut < 0)
				throw new ArgumentOutOfRangeException(nameof(marker));
			if (source == null)
				throw new ArgumentNullException(nameof(source));
			if (maxCellsPerChunk <= 0 || maxCellsPerChunk > SoakGridWire.MaxCellsPerChunk)
				throw new ArgumentOutOfRangeException(nameof(maxCellsPerChunk));
		}

		private static SoakGridReconcileChunkPacket CreateChunk(
			SoakGridMarker marker,
			List<SoakGridCell> allCells,
			SoakGridProof fullProof,
			int index,
			int totalChunks,
			int chunkSize)
		{
			List<SoakGridCell> cells = allCells.Skip(index * chunkSize).Take(chunkSize).ToList();
			return new SoakGridReconcileChunkPacket
			{
				RunId = marker.RunId,
				SampleId = marker.SampleId,
				Generation = marker.Generation,
				WorldUpdateCut = marker.WorldUpdateCut,
				ChunkIndex = index,
				TotalChunks = totalChunks,
				TotalRecords = fullProof.RecordCount,
				Cells = cells,
				ChunkHash = SoakGridProof.HashCells(cells),
				FullGridHash = fullProof.Hash.ToArray(),
			};
		}
	}

	internal sealed class SoakGridChunkSendCursor
	{
		private readonly IReadOnlyList<SoakGridReconcileChunkPacket> _chunks;
		private int _nextIndex;

		internal SoakGridChunkSendCursor(IReadOnlyList<SoakGridReconcileChunkPacket> chunks)
		{
			_chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
			if (_chunks.Count == 0)
				throw new ArgumentException("Grid reconcile requires chunks", nameof(chunks));
		}

		internal bool IsComplete => _nextIndex == _chunks.Count;

		internal bool TryTakeNext(out SoakGridReconcileChunkPacket chunk)
		{
			chunk = null;
			if (IsComplete)
				return false;
			chunk = _chunks[_nextIndex++];
			return true;
		}
	}

	internal enum SoakGridAcceptResult
	{
		Rejected,
		Accepted,
		Duplicate,
	}

	internal enum SoakGridPumpResult
	{
		WaitingForChunks,
		WaitingForApply,
		Complete,
		Aborted,
	}

	internal sealed class SoakGridReconcileSession
	{
		internal const int ScanBudget = 4096;
		internal const int ApplyBudget = 1024;
		private readonly int _maxApplyAttempts;
		private readonly Dictionary<int, SoakGridReconcileChunkPacket> _chunks = new();
		private SoakGridMarker _marker;
		private int _totalChunks;
		private int _totalRecords;
		private byte[] _fullGridHash;
		private List<SoakGridCell> _expectedCells;
		private List<SoakGridCell> _pendingCells;
		private int _scanIndex;
		private int _applyIndex;
		private int _applyAttempts;
		private bool _initialScanComplete;
		private bool _applyIssued;
		private bool _complete;
		private bool _aborted;

		internal SoakGridReconcileSession(int maxApplyAttempts)
		{
			if (maxApplyAttempts <= 0)
				throw new ArgumentOutOfRangeException(nameof(maxApplyAttempts));
			_maxApplyAttempts = maxApplyAttempts;
		}

		internal SoakGridAcceptResult Accept(SoakGridReconcileChunkPacket chunk)
		{
			if (chunk == null || !TryBindMetadata(chunk))
				return SoakGridAcceptResult.Rejected;
			if (_chunks.TryGetValue(chunk.ChunkIndex, out SoakGridReconcileChunkPacket existing))
				return ChunksEqual(existing, chunk)
					? SoakGridAcceptResult.Duplicate
					: SoakGridAcceptResult.Rejected;
			_chunks.Add(chunk.ChunkIndex, chunk);
			return SoakGridAcceptResult.Accepted;
		}

		internal SoakGridPumpResult Pump(
			Func<SoakGridCell, bool> matches,
			Action<SoakGridCell> apply,
			Func<SoakGridProof> captureProof)
		{
			if (_aborted)
				return SoakGridPumpResult.Aborted;
			if (_complete)
				return SoakGridPumpResult.Complete;
			if (_chunks.Count != _totalChunks)
				return SoakGridPumpResult.WaitingForChunks;
			if (!TryPrepareExpectedCells())
				return Abort();
			return TryObserveExact(matches, apply, captureProof);
		}

		internal bool TryBuildAck(out SoakGridReconcileAckPacket ack)
		{
			ack = null;
			if (!_complete)
				return false;
			ack = BuildExactAck(_chunks[_totalChunks - 1]);
			return true;
		}

		internal static SoakGridReconcileAckPacket BuildExactAck(
			SoakGridReconcileChunkPacket chunk)
		{
			if (chunk == null)
				throw new ArgumentNullException(nameof(chunk));
			return new SoakGridReconcileAckPacket
			{
				RunId = chunk.RunId,
				SampleId = chunk.SampleId,
				Generation = chunk.Generation,
				WorldUpdateCut = chunk.WorldUpdateCut,
				ChunkIndex = chunk.ChunkIndex,
				TotalChunks = chunk.TotalChunks,
				TotalRecords = chunk.TotalRecords,
				ChunkHash = chunk.ChunkHash.ToArray(),
				FullGridHash = chunk.FullGridHash.ToArray(),
			};
		}

		private bool TryBindMetadata(SoakGridReconcileChunkPacket chunk)
		{
			var marker = new SoakGridMarker(
				chunk.RunId, chunk.SampleId, chunk.Generation, chunk.WorldUpdateCut);
			if (_chunks.Count == 0)
			{
				_marker = marker;
				_totalChunks = chunk.TotalChunks;
				_totalRecords = chunk.TotalRecords;
				_fullGridHash = chunk.FullGridHash.ToArray();
				return true;
			}
			return _marker.Equals(marker) && _totalChunks == chunk.TotalChunks
			       && _totalRecords == chunk.TotalRecords
			       && _fullGridHash.SequenceEqual(chunk.FullGridHash);
		}

		private bool TryPrepareExpectedCells()
		{
			if (_expectedCells != null)
				return true;
			_expectedCells = _chunks.OrderBy(pair => pair.Key)
				.SelectMany(pair => pair.Value.Cells).ToList();
			if (_expectedCells.Count != _totalRecords
			    || _expectedCells.Select(cell => cell.Cell).Distinct().Count() != _totalRecords
			    || !SoakGridProof.HashCells(_expectedCells).SequenceEqual(_fullGridHash))
				return false;
			_pendingCells = new List<SoakGridCell>();
			return true;
		}

		private SoakGridPumpResult TryObserveExact(
			Func<SoakGridCell, bool> matches,
			Action<SoakGridCell> apply,
			Func<SoakGridProof> captureProof)
		{
			if (!_initialScanComplete)
			{
				ScanInitialMismatches(matches);
				if (!_initialScanComplete)
					return SoakGridPumpResult.WaitingForApply;
				if (_pendingCells.Count == 0)
					return TryCompleteProof(captureProof);
			}
			if (!_applyIssued)
				return ApplyPendingCells(apply);
			return VerifyExactGrid(matches, captureProof);
		}

		private void ScanInitialMismatches(Func<SoakGridCell, bool> matches)
		{
			int end = Math.Min(_scanIndex + ScanBudget, _expectedCells.Count);
			for (; _scanIndex < end; _scanIndex++)
			{
				SoakGridCell cell = _expectedCells[_scanIndex];
				if (!matches(cell))
					_pendingCells.Add(cell);
			}
			if (_scanIndex == _expectedCells.Count)
			{
				_initialScanComplete = true;
				_scanIndex = 0;
			}
		}

		private SoakGridPumpResult ApplyPendingCells(Action<SoakGridCell> apply)
		{
			int end = Math.Min(_applyIndex + ApplyBudget, _pendingCells.Count);
			for (; _applyIndex < end; _applyIndex++)
				apply(_pendingCells[_applyIndex]);
			if (_applyIndex < _pendingCells.Count)
				return SoakGridPumpResult.WaitingForApply;
			_applyIssued = true;
			_pendingCells.Clear();
			_applyIndex = 0;
			_scanIndex = 0;
			return SoakGridPumpResult.WaitingForApply;
		}

		private SoakGridPumpResult VerifyExactGrid(
			Func<SoakGridCell, bool> matches, Func<SoakGridProof> captureProof)
		{
			int end = Math.Min(_scanIndex + ScanBudget, _expectedCells.Count);
			for (; _scanIndex < end; _scanIndex++)
			{
				SoakGridCell cell = _expectedCells[_scanIndex];
				if (!matches(cell))
					_pendingCells.Add(cell);
			}
			if (_scanIndex < _expectedCells.Count)
				return SoakGridPumpResult.WaitingForApply;
			_scanIndex = 0;
			return CompleteOrRetry(captureProof);
		}

		private SoakGridPumpResult TryCompleteProof(Func<SoakGridProof> captureProof)
			=> CompleteOrRetry(captureProof);

		private SoakGridPumpResult CompleteOrRetry(Func<SoakGridProof> captureProof)
		{
			if (_pendingCells.Count == 0)
			{
				SoakGridProof proof = captureProof();
				if (proof.RecordCount == _totalRecords
				    && proof.Hash.SequenceEqual(_fullGridHash))
				{
					_complete = true;
					return SoakGridPumpResult.Complete;
				}
			}
			if (++_applyAttempts >= _maxApplyAttempts)
				return Abort();
			_applyIssued = _pendingCells.Count == 0;
			_applyIndex = 0;
			_scanIndex = 0;
			return SoakGridPumpResult.WaitingForApply;
		}

		private SoakGridPumpResult Abort()
		{
			_aborted = true;
			return SoakGridPumpResult.Aborted;
		}

		private static bool ChunksEqual(
			SoakGridReconcileChunkPacket left, SoakGridReconcileChunkPacket right)
		{
			return left.RunId == right.RunId && left.SampleId == right.SampleId
			       && left.Generation == right.Generation
			       && left.WorldUpdateCut == right.WorldUpdateCut
			       && left.ChunkIndex == right.ChunkIndex
			       && left.TotalChunks == right.TotalChunks && left.TotalRecords == right.TotalRecords
			       && left.ChunkHash.SequenceEqual(right.ChunkHash)
			       && left.FullGridHash.SequenceEqual(right.FullGridHash)
			       && left.Cells.SequenceEqual(right.Cells);
		}
	}

	internal enum SoakGridAckResult
	{
		Rejected,
		Accepted,
		Duplicate,
		Complete,
	}

	internal sealed class SoakGridAckTracker
	{
		private readonly HashSet<ulong> _clients;
		private readonly SoakGridReconcileChunkPacket _proofChunk;
		private readonly HashSet<ulong> _accepted = new();

		internal SoakGridAckTracker(
			IEnumerable<ulong> clients, IEnumerable<SoakGridReconcileChunkPacket> chunks)
		{
			_clients = new HashSet<ulong>(clients);
			Dictionary<int, SoakGridReconcileChunkPacket> byIndex =
				chunks.ToDictionary(chunk => chunk.ChunkIndex);
			if (_clients.Count == 0 || byIndex.Count == 0
			    || !byIndex.TryGetValue(byIndex.Count - 1, out _proofChunk))
				throw new ArgumentException("Grid reconcile tracker requires clients and chunks");
		}

		internal bool IsComplete => _accepted.Count == _clients.Count;

		internal SoakGridAckResult Accept(ulong clientId, SoakGridReconcileAckPacket ack)
		{
			if (!_clients.Contains(clientId) || ack == null
			    || !AckMatches(_proofChunk, ack))
				return SoakGridAckResult.Rejected;
			if (!_accepted.Add(clientId))
				return SoakGridAckResult.Duplicate;
			return IsComplete ? SoakGridAckResult.Complete : SoakGridAckResult.Accepted;
		}

		private static bool AckMatches(
			SoakGridReconcileChunkPacket chunk, SoakGridReconcileAckPacket ack)
		{
			return chunk.RunId == ack.RunId && chunk.SampleId == ack.SampleId
			       && chunk.Generation == ack.Generation
			       && chunk.WorldUpdateCut == ack.WorldUpdateCut
			       && chunk.ChunkIndex == ack.ChunkIndex
			       && chunk.TotalChunks == ack.TotalChunks && chunk.TotalRecords == ack.TotalRecords
			       && chunk.ChunkHash.SequenceEqual(ack.ChunkHash)
			       && chunk.FullGridHash.SequenceEqual(ack.FullGridHash);
		}
	}
}
#endif
