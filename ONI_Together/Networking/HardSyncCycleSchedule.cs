using System;

namespace ONI_Together.Networking
{
	// Counts cycle transitions independently of the save-transfer operation.
	internal sealed class HardSyncCycleSchedule
	{
		private int lastCycle = -1;
		private int lastScheduledCycle;
		private bool enabled;
		private int interval = 1;

		public int Revision { get; private set; }

		public void Reset(int cycle, bool enabled, int interval)
		{
			lastCycle = lastScheduledCycle = cycle;
			this.enabled = enabled;
			this.interval = Math.Max(1, interval);
			Revision++;
		}

		public bool Advance(int cycle, bool enabled, int interval)
		{
			interval = Math.Max(1, interval);
			if (lastCycle < 0 || cycle < lastCycle || this.enabled != enabled || this.interval != interval)
			{
				Reset(cycle, enabled, interval);
				return false;
			}

			if (cycle == lastCycle)
				return false;

			lastCycle = cycle;
			if (!enabled || (long)cycle - lastScheduledCycle < interval)
				return false;

			lastScheduledCycle = cycle;
			Revision++;
			return true;
		}

		public void UpdateSettings(int cycle, bool enabled, int interval)
		{
			if (this.enabled != enabled || this.interval != Math.Max(1, interval))
				Reset(cycle, enabled, interval);
		}

		public bool IsCurrent(int revision, bool enabled, int interval)
		{
			return revision == Revision && enabled && this.enabled && this.interval == Math.Max(1, interval);
		}
	}
}
