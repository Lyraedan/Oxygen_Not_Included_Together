using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace ONI_Together_DedicatedServer.ONI
{
    public sealed class SaveHeader
    {
        private const int MaxHeaderBytes = 1024 * 1024;

        public int BuildVersion { get; private init; }
        public string BaseName { get; private init; } = string.Empty;
        public int Cycles { get; private init; }
        public int DisplayCycle => Cycles + 1;
        public int Duplicants { get; private init; }

        public IReadOnlyList<string> DlcIds { get; private init; } = Array.Empty<string>();

        public static SaveHeader? TryRead(byte[]? save)
        {
            if (save == null || save.Length < 16)
                return null;

            try
            {
                int build = BitConverter.ToInt32(save, 0);
                int size = BitConverter.ToInt32(save, 4);
                int version = BitConverter.ToInt32(save, 8);
                int start = version >= 1 ? 16 : 12;
                if (size <= 0 || size > MaxHeaderBytes || start + size > save.Length)
                    return null;

                var info = JObject.Parse(Encoding.UTF8.GetString(save, start, size));
                var dlcs = (info["dlcIds"] as JArray)?
                    .Select(t => t.Type == JTokenType.String ? ((string?)t)?.Trim() : null)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Select(id => id!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray() ?? Array.Empty<string>();

                return new SaveHeader
                {
                    BuildVersion = build,
                    BaseName = (string?)info["baseName"] ?? string.Empty,
                    Cycles = (int?)info["numberOfCycles"] ?? 0,
                    Duplicants = (int?)info["numberOfDuplicants"] ?? 0,
                    DlcIds = dlcs
                };
            }
            catch (Exception ex) when (ex is Newtonsoft.Json.JsonException || ex is ArgumentException || ex is InvalidCastException || ex is FormatException || ex is OverflowException)
            {
                return null;
            }
        }
    }
}
