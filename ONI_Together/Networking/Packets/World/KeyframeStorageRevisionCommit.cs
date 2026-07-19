#if DEBUG
using System.Collections.Generic;

namespace ONI_Together.Networking.Packets.World
{
	internal sealed class KeyframeStorageRevisionCommit
	{
		private readonly IReadOnlyDictionary<int, ulong> _revisions;

		internal KeyframeStorageRevisionCommit(
			IReadOnlyDictionary<int, ulong> revisions)
		{
			_revisions = revisions == null
				? null
				: new Dictionary<int, ulong>(revisions);
		}

		internal bool TryComplete(bool applySucceeded)
		{
			return applySucceeded
			       && NetworkIdentityRegistry.TryCommitStorageSnapshotRevisions(
				       _revisions);
		}
	}
}
#endif
