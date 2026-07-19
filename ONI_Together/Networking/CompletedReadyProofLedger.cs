using System;
using System.Collections.Generic;

namespace ONI_Together.Networking
{
	internal readonly struct CompletedReadyProof
	{
		public readonly ulong ClientId;
		public readonly ulong ReconnectToken;
		public readonly long SnapshotGeneration;
		public readonly System.DateTime CompletedAtUtc;

		public CompletedReadyProof(
			ulong clientId,
			ulong reconnectToken,
			long snapshotGeneration,
			System.DateTime completedAtUtc)
		{
			ClientId = clientId;
			ReconnectToken = reconnectToken;
			SnapshotGeneration = snapshotGeneration;
			CompletedAtUtc = completedAtUtc;
		}
	}

	internal sealed class CompletedReadyProofLedger
	{
		private readonly Dictionary<ulong, CompletedReadyProof> _proofs = new();
		public int Count => _proofs.Count;

		public bool Record(
			ulong clientId,
			ulong reconnectToken,
			long snapshotGeneration,
			System.DateTime completedAtUtc)
		{
			if (clientId == 0 || reconnectToken == 0 || snapshotGeneration <= 0)
				return false;
			if (_proofs.TryGetValue(reconnectToken, out CompletedReadyProof existing)
			    && (existing.ClientId != clientId
			        || existing.SnapshotGeneration != snapshotGeneration))
				return false;
			_proofs[reconnectToken] = new CompletedReadyProof(
				clientId, reconnectToken, snapshotGeneration, completedAtUtc);
			return true;
		}

		public bool IsExact(ulong clientId, ulong reconnectToken, long snapshotGeneration)
			=> _proofs.TryGetValue(reconnectToken, out CompletedReadyProof proof)
			   && proof.ClientId == clientId
			   && proof.SnapshotGeneration == snapshotGeneration;

		public bool AuthorizesReconnect(
			ulong clientId, ulong reconnectToken, bool requireSameClient)
			=> _proofs.TryGetValue(reconnectToken, out CompletedReadyProof proof)
			   && (!requireSameClient || proof.ClientId == clientId);

		public bool Acknowledge(ulong clientId, ulong reconnectToken, long snapshotGeneration)
		{
			if (!IsExact(clientId, reconnectToken, snapshotGeneration))
				return false;
			return _proofs.Remove(reconnectToken);
		}

		public int Prune(System.DateTime utcNow, System.TimeSpan maximumAge)
		{
			int removed = 0;
			foreach (ulong token in new List<ulong>(_proofs.Keys))
			{
				if (utcNow - _proofs[token].CompletedAtUtc <= maximumAge)
					continue;
				_proofs.Remove(token);
				removed++;
			}
			return removed;
		}

		public void Clear() => _proofs.Clear();
	}
}
