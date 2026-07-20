using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;

namespace ONI_Together.Networking
{
	internal static class ProtocolCompatibility
	{
		public const int CurrentProtocolVersion = 9;

		private static int? _packetFingerprint;
		private static string _modVersion;
		private static string _modBuildFingerprint;

		public static int GameBuild => checked((int)KleiVersion.ChangeList);

		public static int PacketFingerprint
		{
			get
			{
				using var _ = Profiler.Scope();

				return _packetFingerprint ??= PacketRegistry.GetRegisteredPacketFingerprint();
			}
		}

		public static string ModVersion
		{
			get
			{
				using var _ = Profiler.Scope();

				return _modVersion ??= ONI_Together.ModUpdater.Updater.GetFullVersion();
			}
		}

		public static string ModBuildFingerprint
		{
			get
			{
				using var _ = Profiler.Scope();
				return _modBuildFingerprint ??= ComputeAssemblyFingerprint();
			}
		}

		public static HashSet<string> ActiveDlcIds
			=> new(DlcManager.GetActiveDLCIds(), StringComparer.Ordinal);

		public static bool Matches(
			string modBuildFingerprint,
			IEnumerable<string> activeDlcIds)
		{
			using var _ = Profiler.Scope();

			return ModBuildFingerprint.Length == 64
				&& string.Equals(modBuildFingerprint, ModBuildFingerprint, StringComparison.Ordinal)
				&& ActiveDlcIds.SetEquals(activeDlcIds ?? Array.Empty<string>());
		}

		public static string BuildMismatchReason(
			string remoteModBuildFingerprint,
			IEnumerable<string> remoteActiveDlcIds,
			bool hasMetadata)
		{
			using var _ = Profiler.Scope();

			if (!hasMetadata)
			{
				return STRINGS.UI.PROTOCOL.NO_METADATA;
			}

			if (ModBuildFingerprint.Length != 64
			    || !string.Equals(remoteModBuildFingerprint, ModBuildFingerprint, StringComparison.Ordinal))
				return STRINGS.UI.PROTOCOL.MOD_BUILD_MISMATCH;

			if (!ActiveDlcIds.SetEquals(remoteActiveDlcIds ?? Array.Empty<string>()))
			{
				return string.Format(
					STRINGS.UI.PROTOCOL.DLC_MISMATCH,
					FormatDlcIds(ActiveDlcIds),
					FormatDlcIds(remoteActiveDlcIds));
			}

			return STRINGS.UI.PROTOCOL.INCOMPATIBLE;
		}

		internal static string FormatDlcIds(IEnumerable<string> dlcIds)
		{
			string[] ids = (dlcIds ?? Array.Empty<string>())
				.Where(id => !string.IsNullOrEmpty(id))
				.Distinct(StringComparer.Ordinal)
				.OrderBy(id => id, StringComparer.Ordinal)
				.ToArray();
			return ids.Length == 0 ? "Base Game" : string.Join(", ", ids);
		}

		private static string ComputeAssemblyFingerprint()
		{
			string location = typeof(ProtocolCompatibility).Assembly.Location;
			if (string.IsNullOrEmpty(location) || !File.Exists(location))
				return string.Empty;

			using FileStream stream = File.OpenRead(location);
			using SHA256 sha = SHA256.Create();
			byte[] hash = sha.ComputeHash(stream);
			var result = new StringBuilder(hash.Length * 2);
			foreach (byte value in hash)
				result.Append(value.ToString("x2"));
			return result.ToString();
		}

		internal static string ComposeModFingerprint(
			int loadOrder,
			string staticId,
			string platform,
			string id,
			string version,
			string contentHash,
			string configHash)
		{
			return EncodeFingerprintPart(loadOrder.ToString(CultureInfo.InvariantCulture))
			       + EncodeFingerprintPart(staticId)
			       + EncodeFingerprintPart(platform)
			       + EncodeFingerprintPart(id)
			       + EncodeFingerprintPart(version)
			       + EncodeFingerprintPart(contentHash)
			       + EncodeFingerprintPart(configHash);
		}

		private static string EncodeFingerprintPart(string value)
		{
			value ??= string.Empty;
			return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
		}

		internal static string ComputeCanonicalDirectoryHash(string directory)
		{
			if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
				return string.Empty;
			string root = Path.GetFullPath(directory);
			string[] files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
				.Where(path => !IsTransientModFile(path))
				.OrderBy(path => NormalizeRelativePath(root, path), StringComparer.Ordinal)
				.ToArray();
			using SHA256 sha = SHA256.Create();
			using var sink = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write);
			WriteCanonicalFiles(root, files, sink);
			sink.FlushFinalBlock();
			return BitConverter.ToString(sha.Hash).Replace("-", string.Empty).ToLowerInvariant();
		}

		private static void WriteCanonicalFiles(string root, string[] files, Stream output)
		{
			using var writer = new BinaryWriter(output, Encoding.UTF8, true);
			foreach (string file in files)
			{
				string relativePath = NormalizeRelativePath(root, file);
				writer.Write(relativePath);
				writer.Write(new FileInfo(file).Length);
				writer.Flush();
				using FileStream input = File.OpenRead(file);
				input.CopyTo(output);
			}
		}

		private static string NormalizeRelativePath(string root, string file)
			=> Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

		private static bool IsTransientModFile(string path)
		{
			string name = Path.GetFileName(path);
			string extension = Path.GetExtension(path);
			return string.Equals(name, ".DS_Store", StringComparison.OrdinalIgnoreCase)
			       || string.Equals(extension, ".pdb", StringComparison.OrdinalIgnoreCase)
			       || string.Equals(extension, ".log", StringComparison.OrdinalIgnoreCase)
			       || string.Equals(extension, ".tmp", StringComparison.OrdinalIgnoreCase)
			       || string.Equals(extension, ".bak", StringComparison.OrdinalIgnoreCase);
		}

		internal static string GetModConfigDirectory(string contentPath, string staticId)
		{
			if (string.IsNullOrEmpty(contentPath) || string.IsNullOrEmpty(staticId))
				return string.Empty;
			DirectoryInfo current = new DirectoryInfo(Path.GetFullPath(contentPath));
			while (current != null && !File.Exists(Path.Combine(current.FullName, "mods.json")))
				current = current.Parent;
			if (current == null)
				return string.Empty;
			string configRoot = Path.GetFullPath(Path.Combine(current.FullName, "config"));
			string candidate = Path.GetFullPath(Path.Combine(configRoot, staticId));
			return candidate.StartsWith(
				configRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
				? candidate
				: string.Empty;
		}
	}
}
