using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ONI_Together.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class HardSyncScheduleTests
	{
		[UnitTest(name: "Legacy hard-sync settings retain every-cycle behavior", category: "Sync")]
		public static UnitTestResult LegacySettings()
		{
			var legacy = JsonConvert.DeserializeObject<Configuration>("{\"Host\":{\"Server\":{\"HardSyncAtCycleStart\":true}}}");
			var defaults = JsonConvert.DeserializeObject<Configuration>("{}");
			if (!legacy.HardSyncOnCycleStart || legacy.HardSyncIntervalCycles != 1
				|| defaults.HardSyncOnCycleStart || defaults.HardSyncIntervalCycles != 1)
				return UnitTestResult.Fail("Missing interval must default to 1 and preserve the existing toggle");

			return UnitTestResult.Pass();
		}

		[UnitTest(name: "Hard-sync interval persists and rejects nonpositive values", category: "Sync")]
		public static UnitTestResult IntervalSettings()
		{
			var config = new Configuration { HardSyncOnCycleStart = true, HardSyncIntervalCycles = 7 };
			string json = JsonConvert.SerializeObject(config);
			var copy = JsonConvert.DeserializeObject<Configuration>(json);
			var tree = JObject.Parse(json);
			if (!copy.HardSyncOnCycleStart || copy.HardSyncIntervalCycles != 7
				|| (int)tree["Host"]["Server"]["HardSyncIntervalCycles"] != 7
				|| tree["HardSyncIntervalCycles"] != null)
				return UnitTestResult.Fail("Interval must round-trip inside Host.Server without serializing the options proxy");

			foreach (int invalid in new[] { 0, -3, int.MinValue })
			{
				copy.HardSyncIntervalCycles = invalid;
				var loaded = JsonConvert.DeserializeObject<ServerSettings>("{\"HardSyncIntervalCycles\":" + invalid + "}");
				if (copy.HardSyncIntervalCycles != 1 || loaded.HardSyncIntervalCycles != 1)
					return UnitTestResult.Fail("Invalid interval was not clamped to 1");
			}

			return UnitTestResult.Pass();
		}

		[UnitTest(name: "Automatic hard sync occurs only every N cycle transitions", category: "Sync")]
		public static UnitTestResult CycleCadence()
		{
			foreach (int interval in new[] { 1, 3, 7 })
			{
				var schedule = new HardSyncCycleSchedule();
				schedule.Reset(40, true, interval);
				for (int cycle = 40; cycle <= 61; cycle++)
				{
					bool expected = cycle > 40 && (cycle - 40) % interval == 0;
					if (schedule.Advance(cycle, true, interval) != expected
						|| schedule.Advance(cycle, true, interval))
						return UnitTestResult.Fail($"Incorrect cadence or duplicate sync at cycle {cycle}, interval {interval}");
				}
			}

			return UnitTestResult.Pass();
		}

		[UnitTest(name: "Enabling or changing hard-sync interval restarts counting", category: "Sync")]
		public static UnitTestResult SettingsChanges()
		{
			var schedule = new HardSyncCycleSchedule();
			schedule.Reset(10, false, 3);
			if (schedule.Advance(15, false, 3))
				return UnitTestResult.Fail("Disabled automatic sync was scheduled");
			schedule.UpdateSettings(15, true, 3);
			if (schedule.Advance(15, true, 3) || schedule.Advance(17, true, 3)
				|| !schedule.Advance(18, true, 3))
				return UnitTestResult.Fail("Enabling must wait for the configured number of transitions");

			int pendingRevision = schedule.Revision;
			schedule.UpdateSettings(18, true, 5);
			if (schedule.IsCurrent(pendingRevision, true, 5)
				|| schedule.Advance(22, true, 5) || !schedule.Advance(23, true, 5))
				return UnitTestResult.Fail("Changing interval must cancel pending sync and restart counting");

			pendingRevision = schedule.Revision;
			schedule.UpdateSettings(23, false, 5);
			schedule.UpdateSettings(23, true, 5);
			if (schedule.IsCurrent(pendingRevision, true, 5))
				return UnitTestResult.Fail("Disabling and re-enabling must invalidate a delayed sync");

			return UnitTestResult.Pass();
		}

		[UnitTest(name: "Unrelated option changes preserve hard-sync cadence", category: "Sync")]
		public static UnitTestResult UnchangedSettings()
		{
			var schedule = new HardSyncCycleSchedule();
			schedule.Reset(10, true, 3);
			schedule.Advance(11, true, 3);
			schedule.UpdateSettings(11, true, 3);
			if (schedule.Advance(12, true, 3) || !schedule.Advance(13, true, 3))
				return UnitTestResult.Fail("Unchanged settings must preserve the cycle count");
			int revision = schedule.Revision;
			schedule.UpdateSettings(13, true, 3);
			if (!schedule.IsCurrent(revision, true, 3))
				return UnitTestResult.Fail("Unchanged settings must preserve a pending sync");

			return UnitTestResult.Pass();
		}

		[UnitTest(name: "Save load and session reset cancel pending automatic sync", category: "Sync")]
		public static UnitTestResult LifecycleReset()
		{
			var schedule = new HardSyncCycleSchedule();
			schedule.Reset(40, true, 3);
			schedule.Advance(43, true, 3);
			int revision = schedule.Revision;
			schedule.Reset(43, true, 3); // Reloading the same cycle must also invalidate the coroutine.
			if (schedule.IsCurrent(revision, true, 3) || schedule.Advance(45, true, 3)
				|| !schedule.Advance(46, true, 3))
				return UnitTestResult.Fail("Reset must cancel pending sync and restart counting from the loaded cycle");

			revision = schedule.Revision;
			if (schedule.Advance(20, true, 3) || schedule.IsCurrent(revision, true, 3)
				|| schedule.Advance(22, true, 3) || !schedule.Advance(23, true, 3))
				return UnitTestResult.Fail("Clock rewind must reset the schedule");

			schedule.Reset(-1, true, 3); // Hosting before a clock exists.
			if (schedule.Advance(100, true, 3) || schedule.Advance(102, true, 3)
				|| !schedule.Advance(103, true, 3))
				return UnitTestResult.Fail("Uninitialized clock must anchor on its first observed cycle");

			return UnitTestResult.Pass();
		}

		[UnitTest(name: "Skipped cycles never queue multiple automatic syncs", category: "Sync")]
		public static UnitTestResult SkippedCycles()
		{
			var schedule = new HardSyncCycleSchedule();
			schedule.Reset(10, true, 3);
			if (!schedule.Advance(20, true, 3) || schedule.Advance(20, true, 3)
				|| schedule.Advance(22, true, 3) || !schedule.Advance(23, true, 3))
				return UnitTestResult.Fail("Skipped cycles must schedule once and count again from the current cycle");

			schedule.Reset(int.MaxValue - 3, true, 3);
			if (!schedule.Advance(int.MaxValue, true, 3))
				return UnitTestResult.Fail("Cycle arithmetic overflowed");

			return UnitTestResult.Pass();
		}
	}
}
