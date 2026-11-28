using System;
using System.IO;
using System.Text;

namespace ONI_Together_DedicatedServer.Logging
{
    /// <summary>Simple structured console + optional file logger. No secrets are logged.</summary>
    public sealed class ServerLogger
    {
        public enum Level { Trace = 0, Debug = 1, Info = 2, Warn = 3, Error = 4 }

        private readonly object _gate = new();
        private readonly string? _logFile;
        public Level Minimum { get; set; }

        /// <summary>Process-wide logger used by transports. Set by <c>DedicatedServer</c> at startup.</summary>
        public static ServerLogger Current { get; set; } = new();

        public ServerLogger(string? logFile = null, Level minimum = Level.Info)
        {
            _logFile = logFile;
            Minimum = minimum;
            if (!string.IsNullOrWhiteSpace(_logFile))
            {
                try
                {
                    var dir = Path.GetDirectoryName(_logFile);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                }
                catch { /* never crash on logging setup */ }
            }
        }

        public void Trace(string msg) => Log(Level.Trace, msg);
        public void Debug(string msg) => Log(Level.Debug, msg);
        public void Info(string msg) => Log(Level.Info, msg);
        public void Warn(string msg) => Log(Level.Warn, msg);
        public void Error(string msg) => Log(Level.Error, msg);

        public void Log(Level level, string message)
        {
            if (level < Minimum) return;
            string line = $"{DateTime.UtcNow:O} [{level.ToString().ToUpperInvariant()}] {message}";
            lock (_gate)
            {
                Console.WriteLine(line);
                if (!string.IsNullOrWhiteSpace(_logFile))
                {
                    try { File.AppendAllText(_logFile, line + Environment.NewLine, Encoding.UTF8); } catch { /* never crash on logging */ }
                }
            }
        }

        public static Level ParseLevel(string? s) =>
            Enum.TryParse(s, true, out Level lv) ? lv : Level.Info;
    }
}
