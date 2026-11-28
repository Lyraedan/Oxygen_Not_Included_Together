using System;
using System.IO;

namespace ONI_Together_DedicatedServer.ONI
{
    public class SaveFile
    {
        /// <summary>Maximum accepted save size in bytes (256 MiB default).</summary>
        public const long MaxSaveBytes = 256L * 1024 * 1024;

        public byte[] Data { get; set; }
        public string Name { get; set; }

        public SaveFile(string name, byte[] data)
        {
            Name = name;
            Data = data;
        }

        public static SaveFile FromFile(string filePath, long maxBytes = MaxSaveBytes)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Save file path cannot be empty.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Save file not found: {filePath}", filePath);

            var info = new FileInfo(filePath);
            if (info.Length > maxBytes)
                throw new InvalidOperationException($"Save file exceeds maximum size of {maxBytes} bytes: {filePath}");

            string name = Path.GetFileName(filePath);
            byte[] data = File.ReadAllBytes(filePath);

            Logging.ServerLogger.Current.Info($"[SaveFile] Loaded save file: {name} ({Utils.FormatBytes(data.Length)})");
            return new SaveFile(name, data);
        }

        /// <summary>Atomically write the save to <paramref name="filePath"/>.</summary>
        public void SaveToFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Save file path cannot be empty.", nameof(filePath));
            if (Data == null) throw new InvalidOperationException("No data to save.");
            if (Data.LongLength > MaxSaveBytes)
                throw new InvalidOperationException($"Save exceeds maximum size of {MaxSaveBytes} bytes.");

            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string tmp = filePath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllBytes(tmp, Data);
            if (File.Exists(filePath))
            {
                try { File.Replace(tmp, filePath, null, ignoreMetadataErrors: true); }
                catch (PlatformNotSupportedException) { File.Delete(filePath); File.Move(tmp, filePath); }
                catch (IOException) { File.Delete(filePath); File.Move(tmp, filePath); }
            }
            else
            {
                File.Move(tmp, filePath);
            }
        }
    }
}
