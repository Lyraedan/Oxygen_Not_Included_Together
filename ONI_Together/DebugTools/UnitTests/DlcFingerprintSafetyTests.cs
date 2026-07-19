using System;
using System.IO;

namespace ONI_Together.DebugTools.UnitTests;

public static class DlcFingerprintSafetyTests
{
	[UnitTest(name: "DLC handshake: simulation DLC sets must match exactly", category: "Networking")]
	public static UnitTestResult SimulationDlcSetsMustMatchExactly()
	{
		string[] active =
		{
			DlcManager.EXPANSION1_ID, DlcManager.DLC2_ID, DlcManager.DLC3_ID,
			DlcManager.DLC4_ID, DlcManager.DLC5_ID, DlcManager.COSMETIC1_ID
		};
		string[] sameSaveDifferentOrder =
		{
			DlcManager.DLC5_ID, DlcManager.DLC3_ID, DlcManager.DLC2_ID,
			DlcManager.DLC4_ID, DlcManager.EXPANSION1_ID, DlcManager.VANILLA_ID,
			DlcManager.DLC5_ID
		};

		if (!SaveHelper.SimulationDlcSetsMatch(active, sameSaveDifferentOrder))
			return UnitTestResult.Fail("Order, duplicates, vanilla, or cosmetic DLC changed the simulation set");
		if (SaveHelper.SimulationDlcSetsMatch(active, new[]
		    {
			    DlcManager.EXPANSION1_ID, DlcManager.DLC2_ID, DlcManager.DLC3_ID,
			    DlcManager.DLC4_ID
		    }))
			return UnitTestResult.Fail("A save missing an active simulation DLC was accepted");
		if (SaveHelper.SimulationDlcSetsMatch(
		    new[] { DlcManager.EXPANSION1_ID, DlcManager.DLC2_ID },
		    new[] { DlcManager.EXPANSION1_ID, DlcManager.DLC2_ID, DlcManager.DLC3_ID }))
			return UnitTestResult.Fail("A save requiring an inactive simulation DLC was accepted");
		if (SaveHelper.SimulationDlcSetsMatch(
		    new[] { DlcManager.DLC2_ID }, new[] { DlcManager.DLC2_ID.ToLowerInvariant() }))
			return UnitTestResult.Fail("DLC ids were compared case-insensitively");

		return UnitTestResult.Pass("Normalized simulation DLC ids use exact ordinal set equality");
	}

	[UnitTest(name: "Mod fingerprint: cache invalidates on directory state", category: "Networking")]
	public static UnitTestResult ModFingerprintCacheInvalidatesOnDirectoryState()
	{
		string directory = Path.Combine(
			Path.GetTempPath(), "oni-together-fingerprint-" + Guid.NewGuid().ToString("N"));
		string file = Path.Combine(directory, "settings.json");
		try
		{
			Directory.CreateDirectory(directory);
			File.WriteAllText(file, "alpha");
			System.DateTime firstWrite = System.DateTime.UtcNow.AddMinutes(-3);
			File.SetLastWriteTimeUtc(file, firstWrite);
			string firstKey = SaveHelper.ComputeDirectoryFingerprintCacheKey(directory);
			string firstHash = SaveHelper.ComputeCachedDirectoryHash(directory);
			using (FileStream locked = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None))
			{
				if (!string.Equals(
				    firstHash, SaveHelper.ComputeCachedDirectoryHash(directory), StringComparison.Ordinal))
					return UnitTestResult.Fail("Unchanged directory did not reuse its cached fingerprint");
			}

			File.WriteAllText(file, "bravo");
			File.SetLastWriteTimeUtc(file, firstWrite.AddMinutes(1));
			string contentKey = SaveHelper.ComputeDirectoryFingerprintCacheKey(directory);
			string contentHash = SaveHelper.ComputeCachedDirectoryHash(directory);
			if (string.Equals(firstKey, contentKey, StringComparison.Ordinal)
			    || string.Equals(firstHash, contentHash, StringComparison.Ordinal))
				return UnitTestResult.Fail("Same-size content rewrite reused a stale fingerprint");

			File.SetLastWriteTimeUtc(file, firstWrite.AddMinutes(2));
			string mtimeKey = SaveHelper.ComputeDirectoryFingerprintCacheKey(directory);
			string mtimeHash = SaveHelper.ComputeCachedDirectoryHash(directory);
			if (string.Equals(contentKey, mtimeKey, StringComparison.Ordinal))
				return UnitTestResult.Fail("mtime-only change did not invalidate the cache key");
			if (!string.Equals(contentHash, mtimeHash, StringComparison.Ordinal))
				return UnitTestResult.Fail("mtime-only change weakened content-based equality");

			File.WriteAllText(file, "charlie");
			File.SetLastWriteTimeUtc(file, firstWrite.AddMinutes(2));
			string sizeKey = SaveHelper.ComputeDirectoryFingerprintCacheKey(directory);
			string sizeHash = SaveHelper.ComputeCachedDirectoryHash(directory);
			if (string.Equals(mtimeKey, sizeKey, StringComparison.Ordinal)
			    || string.Equals(mtimeHash, sizeHash, StringComparison.Ordinal))
				return UnitTestResult.Fail("Size change reused a stale fingerprint");

			return UnitTestResult.Pass(
				"Cache keys track path, size, and mtime while values remain canonical content hashes");
		}
		finally
		{
			if (Directory.Exists(directory))
				Directory.Delete(directory, true);
		}
	}
}
