using Shared;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class NetworkingHashTests
	{
		[UnitTest(name: "Config keys use stable SDBM hashes", category: "Networking")]
		public static UnitTestResult ConfigKeysUseStableSdbmHashes()
		{
			if (NetworkingHash.ForConfigKey(null) != 0)
				return UnitTestResult.Fail("Null config key must hash to zero like game Hash.SDBMLower");

			var vectors = new[]
			{
				(Key: "SpiceGrinderOption", Expected: -163914648),
				(Key: "DoorUnseal", Expected: 1281215108),
				(Key: "IceMachineElement", Expected: -1843589152),
			};

			foreach (var vector in vectors)
			{
				int actual = NetworkingHash.ForConfigKey(vector.Key);
				if (actual != vector.Expected)
					return UnitTestResult.Fail($"{vector.Key}: expected {vector.Expected}, got {actual}");

				if (actual != Hash.SDBMLower(vector.Key))
					return UnitTestResult.Fail($"{vector.Key}: helper differs from game Hash.SDBMLower");
			}

			return UnitTestResult.Pass("Config keys match stable game SDBM vectors");
		}
	}
}
