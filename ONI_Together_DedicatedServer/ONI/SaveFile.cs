using System;
using System.IO;

namespace ONI_Together_DedicatedServer.ONI
{
    public class SaveFile
    {
        public byte[] Data { get; set; }
        public string Name { get; set; }

        public SaveFile(string name, byte[] data)
        {
            Name = name;
            Data = data;
        }

        public static SaveFile FromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Save file path cannot be empty.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Save file not found: {filePath}", filePath);

            string name = Path.GetFileName(filePath);
            byte[] data = File.ReadAllBytes(filePath);

            Console.WriteLine($"[SaveFile] Loaded save file: {name} ({Utils.FormatBytes(data.Length)})");
            return new SaveFile(name, data);
        }

        public void SaveToFile(string filePath)
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(filePath, Data);
        }
    }
}
