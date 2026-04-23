using ONI_MP.Networking.Packets.Chores;
using Shared.Profiling;
using System.Collections.Generic;

namespace ONI_MP.Networking.Components
{
	public class ClientReceiver_ChoreErrands : KMonoBehaviour
	{
		[MyCmpGet] private NetworkIdentity identity;

		public ErrandEntry? Current { get; private set; }
		public List<ErrandEntry> Upcoming { get; private set; } = new();

		public void Apply(List<ErrandEntry> entries)
		{
			using var _ = Profiler.Scope();
			Current = null;
			if (entries == null)
			{
				Upcoming = new List<ErrandEntry>();
				return;
			}
			var upcoming = new List<ErrandEntry>(entries.Count);
			for (int i = 0; i < entries.Count; i++)
			{
				var entry = entries[i];
				if (entry.IsCurrent && !Current.HasValue)
					Current = entry;
				else
					upcoming.Add(entry);
			}
			Upcoming = upcoming;
		}
	}
}
