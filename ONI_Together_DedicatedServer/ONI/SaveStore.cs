using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ONI_Together_DedicatedServer.Security;

namespace ONI_Together_DedicatedServer.ONI
{
    /// <summary>
    /// Server-owned save storage with atomic writes, backups and retention.
    /// Saves are confined to <see cref="SavesDirectory"/>.
    /// </summary>
    public sealed class SaveStore
    {
        public string SavesDirectory { get; }
        public int BackupCount { get; }
        public long MaxSaveBytes { get; }

        public SaveStore(string savesDirectory, int backupCount = 5, long maxSaveBytes = 256L * 1024 * 1024)
        {
            if (string.IsNullOrWhiteSpace(savesDirectory))
                throw new ArgumentException("Saves directory cannot be empty.", nameof(savesDirectory));
            SavesDirectory = Path.GetFullPath(savesDirectory);
            BackupCount = Math.Max(0, backupCount);
            MaxSaveBytes = Math.Max(1024, maxSaveBytes);
            Directory.CreateDirectory(SavesDirectory);
        }

        public string CurrentSavePath => Path.Combine(SavesDirectory, "current.sav");
        public string BackupsDirectory => Path.Combine(SavesDirectory, "backups");

        /// <summary>Atomically write <paramref name="data"/> to <paramref name="path"/> (must be inside SavesDirectory).</summary>
        public void AtomicWrite(string path, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.LongLength > MaxSaveBytes)
                throw new InvalidOperationException($"Save exceeds maximum size of {MaxSaveBytes} bytes.");

            string? safe = PathSandbox.ResolveInsideRoot(SavesDirectory, path)
                           ?? throw new InvalidOperationException($"Refusing to write outside saves directory: {path}");
            string dir = Path.GetDirectoryName(safe) ?? SavesDirectory;
            Directory.CreateDirectory(dir);

            string tmp = safe + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllBytes(tmp, data);
            // Atomic replace where supported; fall back to move.
            if (File.Exists(safe))
            {
                string backup = safe + ".bak";
                try
                {
                    File.Replace(tmp, safe, backup, ignoreMetadataErrors: true);
                    TryDelete(backup);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(safe, backup, overwrite: true);
                    File.Delete(safe);
                    File.Move(tmp, safe);
                    TryDelete(backup);
                }
                catch (IOException)
                {
                    File.Delete(safe);
                    File.Move(tmp, safe);
                }
            }
            else
            {
                File.Move(tmp, safe);
            }
        }

        /// <summary>Persist server-owned save to current.sav, rotating backups.</summary>
        public string SaveCurrent(byte[] data, string baseName = "current")
        {
            string fileName = PathSandbox.IsSafeFileName(baseName) ? baseName : "current";
            string target = Path.Combine(SavesDirectory, fileName + ".sav");
            string safeTarget = PathSandbox.ResolveInsideRoot(SavesDirectory, target)
                                ?? throw new InvalidOperationException("Unsafe save target.");

            RotateBackups(safeTarget);
            AtomicWrite(safeTarget, data);
            return safeTarget;
        }

        public void RotateBackups(string targetPath)
        {
            if (BackupCount <= 0) return;
            if (!File.Exists(targetPath)) return;

            string safeTarget = PathSandbox.ResolveInsideRoot(SavesDirectory, targetPath)
                                ?? throw new InvalidOperationException("Unsafe save target.");
            Directory.CreateDirectory(BackupsDirectory);
            string stem = Path.GetFileNameWithoutExtension(safeTarget);

            // Shift existing backups.
            for (int i = BackupCount - 1; i >= 1; i--)
            {
                string from = BackupPath(stem, i);
                string to = BackupPath(stem, i + 1);
                if (File.Exists(from))
                {
                    TryDelete(to);
                    File.Move(from, to);
                }
            }
            string first = BackupPath(stem, 1);
            TryDelete(first);
            File.Copy(safeTarget, first, overwrite: true);

            // Trim beyond retention.
            foreach (var file in Directory.EnumerateFiles(BackupsDirectory, stem + ".*.sav").OrderByDescending(f => f))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (int.TryParse(name.Substring(stem.Length + 1), out int idx) && idx > BackupCount)
                    TryDelete(file);
            }
        }

        private string BackupPath(string stem, int index) => Path.Combine(BackupsDirectory, $"{stem}.{index}.sav");

        public IReadOnlyList<string> ListSaves()
        {
            if (!Directory.Exists(SavesDirectory)) return Array.Empty<string>();
            return Directory.EnumerateFiles(SavesDirectory, "*.sav", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public byte[]? TryReadSave(string name)
        {
            if (!PathSandbox.IsSafeFileName(name)) return null;
            string candidate = name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) ? name : name + ".sav";
            string? safe = PathSandbox.ResolveInsideRoot(SavesDirectory, Path.Combine(SavesDirectory, candidate));
            if (safe == null || !File.Exists(safe)) return null;
            var info = new FileInfo(safe);
            if (info.Length > MaxSaveBytes) return null;
            return File.ReadAllBytes(safe);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
        }
    }
}
