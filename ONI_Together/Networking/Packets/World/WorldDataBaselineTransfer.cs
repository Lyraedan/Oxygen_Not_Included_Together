using System;
using System.Collections.Generic;

namespace ONI_Together.Networking.Packets.World
{
	internal sealed class WorldDataSendWindow
	{
		internal const int MaxInFlightChunks = 2;

		private readonly int _totalChunks;
		private int _highestAppliedChunk = -1;
		private int _nextChunkToSend;

		internal WorldDataSendWindow(int totalChunks)
		{
			if (totalChunks <= 0 || totalChunks > WorldDataPacket.MaxChunkCount)
				throw new ArgumentOutOfRangeException(nameof(totalChunks));
			_totalChunks = totalChunks;
		}

		internal bool TrySendAvailable(Func<int, bool> send)
		{
			if (send == null)
				return false;
			while (_nextChunkToSend < _totalChunks
			       && InFlightChunks < MaxInFlightChunks)
			{
				if (!send(_nextChunkToSend))
					return false;
				_nextChunkToSend++;
			}
			return true;
		}

		internal bool AcceptProgress(int appliedThroughChunk)
		{
			if (appliedThroughChunk != _highestAppliedChunk + 1
			    || appliedThroughChunk < 0 || appliedThroughChunk >= _nextChunkToSend)
				return false;
			_highestAppliedChunk = appliedThroughChunk;
			return true;
		}

		internal int InFlightChunks => _nextChunkToSend - _highestAppliedChunk - 1;
		internal bool IsComplete => _highestAppliedChunk == _totalChunks - 1;
	}

	internal sealed class WorldDataLifecycleCollector
	{
		private readonly int _expectedEntries;
		private readonly List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry> _entries;
		private readonly HashSet<int> _netIds = new();

		internal WorldDataLifecycleCollector(int expectedEntries)
		{
			if (expectedEntries < 0
			    || expectedEntries > WorldDataPacket.MaxLifecycleBaselineEntries)
				throw new ArgumentOutOfRangeException(nameof(expectedEntries));
			_expectedEntries = expectedEntries;
			_entries = new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>(expectedEntries);
		}

		internal bool TryAppend(
			IReadOnlyList<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry> page,
			bool isFinalPage)
		{
			if (page == null || page.Count > WorldDataPacket.MaxLifecycleEntriesPerPacket
			    || _entries.Count > _expectedEntries - page.Count)
				return false;
			var pageNetIds = new HashSet<int>();
			foreach (NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry entry in page)
			{
				if (!WorldLifecycleBaselineCodec.IsValidTransferEntry(entry)
				    || _netIds.Contains(entry.NetId) || !pageNetIds.Add(entry.NetId))
					return false;
			}
			bool completes = _entries.Count + page.Count == _expectedEntries;
			if (isFinalPage != completes)
				return false;
			foreach (NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry entry in page)
			{
				_netIds.Add(entry.NetId);
				_entries.Add(entry);
			}
			return true;
		}

		internal bool IsComplete => _entries.Count == _expectedEntries;
		internal IReadOnlyList<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry> Entries => _entries;
	}
}
