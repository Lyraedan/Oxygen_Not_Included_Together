using ONI_Together.Networking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public static partial class SaveHelper
{
	private static readonly object ModFingerprintCacheLock = new();
	private static readonly Dictionary<string, KeyValuePair<string, string>> ModFingerprintCache =
		new(StringComparer.Ordinal);

	internal static bool SimulationDlcSetsMatch(
		IEnumerable<string> activeDlcIds,
		IEnumerable<string> saveDlcIds)
		=> SimulationDlcSetsMatch(activeDlcIds, saveDlcIds, out _, out _);

	internal static bool SimulationDlcSetsMatch(
		IEnumerable<string> activeDlcIds,
		IEnumerable<string> saveDlcIds,
		out HashSet<string> activeSimulationDlcIds,
		out HashSet<string> saveSimulationDlcIds)
	{
		activeSimulationDlcIds = NormalizeSimulationDlcIds(activeDlcIds);
		saveSimulationDlcIds = NormalizeSimulationDlcIds(saveDlcIds);
		return activeSimulationDlcIds.SetEquals(saveSimulationDlcIds);
	}

	internal static HashSet<string> NormalizeSimulationDlcIds(IEnumerable<string> dlcIds)
	{
		var normalized = new HashSet<string>(StringComparer.Ordinal);
		foreach (string id in dlcIds ?? Enumerable.Empty<string>())
		{
			if (!DlcManager.IsVanillaId(id) && !DlcManager.CONTENT_ONLY_DLC_IDS.Contains(id))
				normalized.Add(id);
		}
		return normalized;
	}

	internal static string ComputeCachedDirectoryHash(string directory)
	{
		if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
			return string.Empty;

		string root = Path.GetFullPath(directory);
		string stateKey = ComputeDirectoryFingerprintCacheKey(root);
		lock (ModFingerprintCacheLock)
		{
			if (ModFingerprintCache.TryGetValue(root, out var cached)
			    && string.Equals(cached.Key, stateKey, StringComparison.Ordinal))
				return cached.Value;
		}

		string contentHash = ProtocolCompatibility.ComputeCanonicalDirectoryHash(root);
		lock (ModFingerprintCacheLock)
			ModFingerprintCache[root] = new KeyValuePair<string, string>(stateKey, contentHash);
		return contentHash;
	}

	internal static string ComputeDirectoryFingerprintCacheKey(string directory)
	{
		if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
			return string.Empty;

		string root = Path.GetFullPath(directory);
		string[] files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
			.Where(path => !IsTransientFingerprintFile(path))
			.OrderBy(path => NormalizeFingerprintPath(root, path), StringComparer.Ordinal)
			.ToArray();
		using SHA256 sha = SHA256.Create();
		using var sink = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write);
		using (var writer = new BinaryWriter(sink, Encoding.UTF8, true))
		{
			writer.Write(root);
			foreach (string file in files)
			{
				var info = new FileInfo(file);
				writer.Write(NormalizeFingerprintPath(root, file));
				writer.Write(info.Length);
				writer.Write(info.LastWriteTimeUtc.Ticks);
			}
		}
		sink.FlushFinalBlock();
		return BitConverter.ToString(sha.Hash).Replace("-", string.Empty).ToLowerInvariant();
	}

	private static string NormalizeFingerprintPath(string root, string file)
		=> Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

	private static bool IsTransientFingerprintFile(string path)
	{
		string name = Path.GetFileName(path);
		string extension = Path.GetExtension(path);
		return string.Equals(name, ".DS_Store", StringComparison.OrdinalIgnoreCase)
		       || string.Equals(extension, ".pdb", StringComparison.OrdinalIgnoreCase)
		       || string.Equals(extension, ".log", StringComparison.OrdinalIgnoreCase)
		       || string.Equals(extension, ".tmp", StringComparison.OrdinalIgnoreCase)
		       || string.Equals(extension, ".bak", StringComparison.OrdinalIgnoreCase);
	}
}
